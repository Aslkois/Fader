using System.Collections.Concurrent;
using Fader.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Fader.Audio;

/// <summary>
/// Windows Core Audio backend (WASAPI session API through NAudio's COM wrappers).
///
/// Threading: one dedicated MTA thread creates, uses and releases every COM object. COM callbacks arrive on
/// arbitrary RPC threads and only enqueue work onto that thread, so a callback can never deadlock against a
/// call we are making. Volume writes from the UI are coalesced to the latest value per target. There is no
/// polling except a slow safety re-scan; meters are sampled only while <see cref="SetMetersEnabled"/> is on.
/// </summary>
public sealed class WasapiAudioEngine : IAudioEngine
{
    private const int MeterIntervalMs = 50;
    private const int ReconcileDebounceMs = 150;
    private const int SafetyRescanMs = 45_000;
    private const int RetryMs = 3_000;

    private readonly BlockingCollection<Action> _queue = new();
    private readonly ConcurrentDictionary<string, float> _pendingSessionVolumes = new();
    private readonly AudioEndpointVolumeNotificationDelegate _onEndpointVolume;
    private Thread? _thread;
    private int _drainQueued;
    private int _endpointEmitQueued;
    private float _pendingMaster = -1f;

    // ---- engine-thread state ----
    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notifications;
    private readonly Dictionary<string, DeviceMonitor> _monitors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private DeviceMonitor? _activeMonitor;
    private AudioEndpointVolume? _endpointVolume;
    private bool _activeIsFallback;
    private string? _defaultMultimedia;
    private string? _defaultCommunications;
    private IReadOnlyList<AudioDevice> _lastDevices = [];
    private OutputPreference _preference = new(OutputMode.FollowMultimedia, null);
    private bool _includeOther;
    private bool _metersEnabled;
    private long _nextMeterAt;
    private long _reconcileAt;
    private long _rescanAt;
    private int _sessionsVersion;
    private int _meterVersion = -1;
    private string[] _meterKeys = [];
    private SessionEntry[] _meterEntries = [];
    private EngineStatus _status = EngineStatus.Starting;
    private string? _statusMessage;

    public WasapiAudioEngine()
    {
        _onEndpointVolume = _ => QueueEndpointEmit();
    }

    public event Action<IReadOnlyList<AudioDevice>>? DevicesChanged;
    public event Action<EndpointState?>? EndpointChanged;
    public event Action<SessionUpdate>? SessionUpdated;
    public event Action<string>? SessionRemoved;
    public event Action<MeterFrame>? MetersUpdated;
    public event Action<EngineStatus, string?>? StatusChanged;
    public event Action? InitialScanCompleted;

    public void Start(OutputPreference preference, bool includeOtherDevices)
    {
        if (_thread is not null) throw new InvalidOperationException("Engine already started.");
        _preference = preference;
        _includeOther = includeOtherDevices;
        _thread = new Thread(Run) { IsBackground = true, Name = "FADER audio" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    // ---- commands (any thread) ----

    public void SetOutputPreference(OutputPreference preference) => Enqueue(() =>
    {
        _preference = preference;
        _reconcileAt = Environment.TickCount64;
    });

    public void SetIncludeOtherDevices(bool include) => Enqueue(() =>
    {
        _includeOther = include;
        _reconcileAt = Environment.TickCount64;
    });

    public void SetMetersEnabled(bool enabled) => Enqueue(() =>
    {
        _metersEnabled = enabled;
        if (!enabled) return;
        _nextMeterAt = Environment.TickCount64;
        // Window just became visible: catch up on anything missed while nobody was looking.
        ScanAll();
    });

    public void SetMasterVolume(float volume)
    {
        Volatile.Write(ref _pendingMaster, Math.Clamp(volume, 0f, 1f));
        QueueDrain();
    }

    public void SetMasterMute(bool muted) => Enqueue(() =>
    {
        try
        {
            if (_endpointVolume is not null) _endpointVolume.Mute = muted;
        }
        catch (Exception ex)
        {
            HandleComFailure(ex);
        }
    });

    public void SetSessionVolume(string sessionKey, float volume)
    {
        _pendingSessionVolumes[sessionKey] = Math.Clamp(volume, 0f, 1f);
        QueueDrain();
    }

    public void SetSessionMute(string sessionKey, bool muted) => Enqueue(() =>
    {
        if (!_sessions.TryGetValue(sessionKey, out var entry)) return;
        try
        {
            entry.WriteMute(muted);
        }
        catch (Exception ex)
        {
            HandleComFailure(ex);
        }
    });

    public void Refresh() => Enqueue(() =>
    {
        _reconcileAt = Environment.TickCount64;
        ScanAll();
    });

    internal void Enqueue(Action work)
    {
        try
        {
            _queue.Add(work);
        }
        catch (InvalidOperationException)
        {
            // Shutting down.
        }
    }

    internal void RequestReconcile() => Enqueue(() =>
    {
        if (_reconcileAt == 0) _reconcileAt = Environment.TickCount64 + ReconcileDebounceMs;
    });

    private void RequestScan(string deviceId) => Enqueue(() =>
    {
        if (_monitors.TryGetValue(deviceId, out var monitor)) ScanMonitor(monitor);
    });

    private void QueueDrain()
    {
        if (Interlocked.CompareExchange(ref _drainQueued, 1, 0) == 0) Enqueue(Drain);
    }

    private void QueueEndpointEmit()
    {
        if (Interlocked.CompareExchange(ref _endpointEmitQueued, 1, 0) == 0)
            Enqueue(() =>
            {
                Interlocked.Exchange(ref _endpointEmitQueued, 0);
                EmitEndpoint();
            });
    }

    // ---- engine thread ----

    private void Run()
    {
        try
        {
            Initialize();
            _rescanAt = Environment.TickCount64 + SafetyRescanMs;
            while (!_queue.IsCompleted)
            {
                if (_queue.TryTake(out var work, ComputeWaitMs())) Guarded(work);
                RunDueTimers();
            }
        }
        finally
        {
            Cleanup();
        }
    }

    private void Initialize()
    {
        TryCreateEnumerator();
        Guarded(Reconcile);
        InitialScanCompleted?.Invoke();
    }

    private bool TryCreateEnumerator()
    {
        if (_enumerator is not null) return true;
        try
        {
            var enumerator = new MMDeviceEnumerator();
            // Callbacks arrive on a Windows audio worker thread that holds a lock: they must only enqueue.
            var notifications = enumerator.CreateNotificationClient(useSynchronizationContext: false);
            notifications.DeviceAdded += (_, _) => RequestReconcile();
            notifications.DeviceRemoved += (_, _) => RequestReconcile();
            notifications.DeviceStateChanged += (_, _) => RequestReconcile();
            notifications.DefaultDeviceChanged += (_, e) => { if (e.Flow == DataFlow.Render) RequestReconcile(); };
            _enumerator = enumerator;
            _notifications = notifications;
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(EngineStatus.Error, "Windows audio is not responding. " + ex.Message);
            _reconcileAt = Environment.TickCount64 + RetryMs;
            return false;
        }
    }

    private int ComputeWaitMs()
    {
        var now = Environment.TickCount64;
        var next = _rescanAt;
        if (_reconcileAt != 0 && _reconcileAt < next) next = _reconcileAt;
        if (_metersEnabled && _nextMeterAt < next) next = _nextMeterAt;
        return (int)Math.Clamp(next - now, 0, SafetyRescanMs);
    }

    private void RunDueTimers()
    {
        var now = Environment.TickCount64;
        if (_reconcileAt != 0 && now >= _reconcileAt)
        {
            _reconcileAt = 0;
            Guarded(Reconcile);
        }
        if (_metersEnabled && now >= _nextMeterAt)
        {
            _nextMeterAt = now + MeterIntervalMs;
            Guarded(PumpMeters);
        }
        if (now >= _rescanAt)
        {
            _rescanAt = now + SafetyRescanMs;
            Guarded(ScanAll);
        }
    }

    private void Guarded(Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            HandleComFailure(ex);
        }
    }

    private void HandleComFailure(Exception ex)
    {
        // Most failures here mean a device or session vanished mid-call. Re-evaluate the world shortly.
        System.Diagnostics.Debug.WriteLine("FADER audio: " + ex.Message);
        if (_reconcileAt == 0) _reconcileAt = Environment.TickCount64 + ReconcileDebounceMs;
    }

    private void Drain()
    {
        Interlocked.Exchange(ref _drainQueued, 0);

        var master = Interlocked.Exchange(ref _pendingMaster, -1f);
        if (master >= 0f && _endpointVolume is not null)
        {
            try { _endpointVolume.MasterVolumeLevelScalar = master; }
            catch (Exception ex) { HandleComFailure(ex); }
        }

        foreach (var key in _pendingSessionVolumes.Keys)
        {
            if (!_pendingSessionVolumes.TryRemove(key, out var volume)) continue;
            if (!_sessions.TryGetValue(key, out var entry)) continue;
            try { entry.WriteVolume(volume); }
            catch (Exception ex) { HandleComFailure(ex); }
        }
    }

    // ---- devices ----

    private void Reconcile()
    {
        if (!TryCreateEnumerator()) return;
        var enumerator = _enumerator!;

        _defaultMultimedia = TryDefaultId(enumerator, Role.Multimedia);
        _defaultCommunications = TryDefaultId(enumerator, Role.Communications);

        var devices = new List<AudioDevice>();
        using (var collection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            foreach (var device in collection)
            {
                using (device)
                {
                    devices.Add(new AudioDevice(device.ID, SafeName(device), device.ID == _defaultMultimedia, device.ID == _defaultCommunications));
                }
            }
        }
        devices.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        var ids = devices.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        var activeId = PickActiveDevice(ids, out _activeIsFallback);

        var wanted = new HashSet<string>(StringComparer.Ordinal);
        if (activeId is not null) wanted.Add(activeId);
        if (_includeOther) foreach (var id in ids) wanted.Add(id);

        foreach (var id in _monitors.Keys.Where(k => !wanted.Contains(k)).ToList()) RemoveMonitor(id, emit: true);
        foreach (var id in wanted) if (!_monitors.ContainsKey(id)) AddMonitor(id);

        BindActiveEndpoint(activeId);

        if (!SameDevices(devices, _lastDevices))
        {
            _lastDevices = devices;
            DevicesChanged?.Invoke(devices);
        }

        if (activeId is null || _activeMonitor is null)
            SetStatus(EngineStatus.NoDevice, "No audio output device is available.");
        else
            SetStatus(EngineStatus.Running, null);

        EmitEndpoint();
    }

    private string? PickActiveDevice(HashSet<string> ids, out bool fallback)
    {
        fallback = false;
        string? id;
        switch (_preference.Mode)
        {
            case OutputMode.Specific when !string.IsNullOrEmpty(_preference.DeviceId):
                if (ids.Contains(_preference.DeviceId))
                {
                    id = _preference.DeviceId;
                }
                else
                {
                    fallback = true;
                    id = _defaultMultimedia ?? _defaultCommunications;
                }
                break;
            case OutputMode.FollowCommunications:
                id = _defaultCommunications ?? _defaultMultimedia;
                break;
            default:
                id = _defaultMultimedia ?? _defaultCommunications;
                break;
        }
        if (id is null || !ids.Contains(id)) id = ids.FirstOrDefault();
        return id;
    }

    private void BindActiveEndpoint(string? activeId)
    {
        DeviceMonitor? monitor = null;
        if (activeId is not null) _monitors.TryGetValue(activeId, out monitor);

        if (ReferenceEquals(monitor, _activeMonitor) && (monitor is null || _endpointVolume is not null)) return;

        if (_endpointVolume is not null)
        {
            try { _endpointVolume.OnVolumeNotification -= _onEndpointVolume; } catch (Exception) { }
            _endpointVolume = null;
        }

        _activeMonitor = monitor;
        if (monitor is null) return;

        try
        {
            _endpointVolume = monitor.Device.AudioEndpointVolume;
            _endpointVolume.OnVolumeNotification += _onEndpointVolume;
        }
        catch (Exception ex)
        {
            _endpointVolume = null;
            HandleComFailure(ex);
        }
    }

    private void EmitEndpoint()
    {
        if (_activeMonitor is null || _endpointVolume is null)
        {
            EndpointChanged?.Invoke(null);
            return;
        }

        try
        {
            var id = _activeMonitor.Id;
            EndpointChanged?.Invoke(new EndpointState(
                id,
                SafeName(_activeMonitor.Device),
                _endpointVolume.MasterVolumeLevelScalar,
                _endpointVolume.Mute,
                _activeIsFallback,
                id == _defaultMultimedia,
                id == _defaultCommunications));
        }
        catch (Exception ex)
        {
            HandleComFailure(ex);
        }
    }

    private static string? TryDefaultId(MMDeviceEnumerator enumerator, Role role)
    {
        try
        {
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, role)) return null;
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            return device.ID;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SafeName(MMDevice device)
    {
        try { return device.FriendlyName; }
        catch (Exception) { return "Audio device"; }
    }

    private static bool SameDevices(IReadOnlyList<AudioDevice> a, IReadOnlyList<AudioDevice> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private void SetStatus(EngineStatus status, string? message)
    {
        if (_status == status && _statusMessage == message) return;
        _status = status;
        _statusMessage = message;
        StatusChanged?.Invoke(status, message);
    }

    // ---- monitors and sessions ----

    private void AddMonitor(string id)
    {
        try
        {
            var device = _enumerator!.GetDevice(id);
            var manager = device.AudioSessionManager;
            var monitor = new DeviceMonitor(id, device, manager, (_, _) => RequestScan(id));
            _monitors[id] = monitor;
            ScanMonitor(monitor);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("FADER audio: cannot open device: " + ex.Message);
        }
    }

    private void RemoveMonitor(string id, bool emit)
    {
        if (!_monitors.Remove(id, out var monitor)) return;
        foreach (var key in monitor.SessionKeys.ToList()) RemoveSession(key, emit);
        if (ReferenceEquals(monitor, _activeMonitor))
        {
            if (_endpointVolume is not null)
            {
                try { _endpointVolume.OnVolumeNotification -= _onEndpointVolume; } catch (Exception) { }
                _endpointVolume = null;
            }
            _activeMonitor = null;
        }
        monitor.Dispose();
    }

    private void ScanAll()
    {
        foreach (var monitor in _monitors.Values.ToList()) ScanMonitor(monitor);
    }

    private void ScanMonitor(DeviceMonitor monitor)
    {
        SessionCollection sessions;
        try
        {
            monitor.Manager.RefreshSessions();
            sessions = monitor.Manager.Sessions;
        }
        catch (Exception ex)
        {
            HandleComFailure(ex);
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < sessions.Count; i++)
        {
            AudioSessionControl control;
            string instanceId;
            try
            {
                control = sessions[i];
                instanceId = control.GetSessionInstanceIdentifier;
            }
            catch (Exception)
            {
                continue;
            }

            var key = monitor.Id + "|" + instanceId;
            if (_sessions.TryGetValue(key, out var existing))
            {
                seen.Add(key);
                try { control.Dispose(); } catch (Exception) { }
                Poll(existing);
                continue;
            }

            var entry = SessionEntry.TryCreate(this, monitor, control, key);
            if (entry is null)
            {
                try { control.Dispose(); } catch (Exception) { }
                continue;
            }
            if (entry.State == Core.Audio.SessionState.Expired)
            {
                entry.Dispose();
                continue;
            }

            seen.Add(key);
            _sessions[key] = entry;
            monitor.SessionKeys.Add(key);
            _sessionsVersion++;
            Emit(entry, isNew: true, external: false);
        }

        foreach (var key in monitor.SessionKeys.Where(k => !seen.Contains(k)).ToList()) RemoveSession(key, emit: true);
    }

    /// <summary>Re-reads one session's state; used by safety re-scans in case a callback was missed.</summary>
    private void Poll(SessionEntry entry)
    {
        try
        {
            var state = SessionEntry.MapState(entry.Control.State);
            var volume = entry.Control.SimpleAudioVolume.Volume;
            var muted = entry.Control.SimpleAudioVolume.Mute;
            var external = entry.IsExternalChange(volume, muted);
            var changed = state != entry.State || Math.Abs(volume - entry.Volume) > 0.0005f || muted != entry.Muted;
            entry.State = state;
            entry.Volume = volume;
            entry.Muted = muted;
            if (state == Core.Audio.SessionState.Expired) RemoveSession(entry.Key, emit: true);
            else if (changed) Emit(entry, isNew: false, external);
        }
        catch (Exception)
        {
            RemoveSession(entry.Key, emit: true);
        }
    }

    private void RemoveSession(string key, bool emit)
    {
        if (!_sessions.Remove(key, out var entry)) return;
        entry.Monitor.SessionKeys.Remove(key);
        _sessionsVersion++;
        entry.Dispose();
        if (emit) SessionRemoved?.Invoke(key);
    }

    private void Emit(SessionEntry entry, bool isNew, bool external) =>
        SessionUpdated?.Invoke(new SessionUpdate(entry.ToInfo(), isNew, external));

    // ---- session callbacks, already on the engine thread ----

    internal void HandleVolume(SessionEntry entry, float volume, bool muted)
    {
        if (entry.Disposed || !_sessions.ContainsKey(entry.Key)) return;

        var external = entry.IsExternalChange(volume, muted);
        var volumeDiffers = Math.Abs(volume - entry.Volume) > 0.0005f;
        // An echo of one of our earlier writes that has since been superseded: the newer value is already known.
        if (!external && volumeDiffers) return;

        var changed = volumeDiffers || muted != entry.Muted;
        entry.Volume = volume;
        entry.Muted = muted;
        if (changed) Emit(entry, isNew: false, external);
    }

    internal void HandleState(SessionEntry entry, Core.Audio.SessionState state)
    {
        if (entry.Disposed || !_sessions.ContainsKey(entry.Key)) return;
        if (state == Core.Audio.SessionState.Expired)
        {
            RemoveSession(entry.Key, emit: true);
            return;
        }
        if (state == entry.State) return;
        entry.State = state;
        Emit(entry, isNew: false, external: false);
    }

    internal void HandleDisconnected(SessionEntry entry, AudioSessionDisconnectReason reason)
    {
        if (entry.Disposed) return;
        RemoveSession(entry.Key, emit: true);
        ScanMonitor(entry.Monitor);
        RequestReconcile();
    }

    internal void HandleDisplayName(SessionEntry entry, string name)
    {
        if (entry.Disposed) return;
        entry.RenameSession(name);
        Emit(entry, isNew: false, external: false);
    }

    // ---- meters ----

    private void PumpMeters()
    {
        if (_meterVersion != _sessionsVersion)
        {
            _meterEntries = _sessions.Values.ToArray();
            _meterKeys = _meterEntries.Select(e => e.Key).ToArray();
            _meterVersion = _sessionsVersion;
        }

        var peaks = new float[_meterEntries.Length];
        for (var i = 0; i < _meterEntries.Length; i++)
        {
            var entry = _meterEntries[i];
            if (entry.Disposed || entry.State != Core.Audio.SessionState.Active || entry.Meter is null) continue;
            try { peaks[i] = entry.Meter.MasterPeakValue; }
            catch (Exception) { /* session ended between ticks */ }
        }

        var master = 0f;
        if (_activeMonitor is not null)
        {
            try { master = _activeMonitor.Device.AudioMeterInformation.MasterPeakValue; }
            catch (Exception) { /* device went away; reconcile will follow */ }
        }

        MetersUpdated?.Invoke(new MeterFrame(master, _meterKeys, peaks));
    }

    // ---- shutdown ----

    private void Cleanup()
    {
        try { _notifications?.Dispose(); } catch (Exception) { }

        foreach (var id in _monitors.Keys.ToList()) RemoveMonitor(id, emit: false);
        _sessions.Clear();

        try { _enumerator?.Dispose(); } catch (Exception) { }
        _enumerator = null;
        _notifications = null;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread?.Join(TimeSpan.FromSeconds(5));
        _queue.Dispose();
    }
}
