using Fader.Core.Apps;

namespace Fader.Core.Audio;

/// <summary>
/// Backend-agnostic mixer state. Subscribes to an <see cref="IAudioEngine"/>, groups its sessions into
/// one <see cref="AppGroup"/> per application, and re-raises changes on the supplied
/// <see cref="SynchronizationContext"/> (the UI thread in the app, inline in tests).
/// All members other than the engine callbacks must be used from that context.
/// </summary>
public sealed class MixerService : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly SynchronizationContext? _sync;
    private readonly Dictionary<string, AppGroup> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sessionToGroup = new(StringComparer.Ordinal);
    private int _meterPending;
    private volatile bool _disposed;

    public MixerService(IAudioEngine engine, SynchronizationContext? sync)
    {
        _engine = engine;
        _sync = sync;
        engine.DevicesChanged += OnDevices;
        engine.EndpointChanged += OnEndpoint;
        engine.SessionUpdated += OnSessionUpdated;
        engine.SessionRemoved += OnSessionRemoved;
        engine.MetersUpdated += OnMeters;
        engine.StatusChanged += OnStatus;
        engine.InitialScanCompleted += OnInitialScan;
    }

    public bool IsReady { get; private set; }
    public EngineStatus Status { get; private set; } = EngineStatus.Starting;
    public string? StatusMessage { get; private set; }
    public EndpointState? Endpoint { get; private set; }
    public IReadOnlyList<AudioDevice> Devices { get; private set; } = [];
    public float MasterPeak { get; private set; }
    public IReadOnlyCollection<AppGroup> Groups => _groups.Values;

    public event Action<AppGroup>? GroupAdded;
    public event Action<AppGroup>? GroupRemoved;
    public event Action<AppGroup>? GroupChanged;
    public event Action<AppGroup, SessionInfo>? SessionAdded;
    public event Action<AppGroup>? ExternalChange;
    public event Action? MetersUpdated;
    public event Action? EndpointChanged;
    public event Action? DevicesChanged;
    public event Action? StatusChanged;

    public bool TryGetGroup(string key, out AppGroup group) => _groups.TryGetValue(key, out group!);

    public AppGroup? FindGroup(AppIdentity identity)
    {
        if (_groups.TryGetValue(identity.Key, out var exact)) return exact;
        foreach (var g in _groups.Values) if (g.Identity.Matches(identity)) return g;
        return null;
    }

    // ---- commands (UI thread) ----

    public void SetGroupVolume(AppGroup group, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        var changed = false;
        foreach (var s in group.SessionMap.Values.ToArray())
        {
            _engine.SetSessionVolume(s.Key, volume);
            if (Math.Abs(s.Volume - volume) > 0.0005f)
            {
                group.SessionMap[s.Key] = s with { Volume = volume };
                changed = true;
            }
        }
        if (changed) GroupChanged?.Invoke(group);
    }

    public void SetGroupMute(AppGroup group, bool muted)
    {
        var changed = false;
        foreach (var s in group.SessionMap.Values.ToArray())
        {
            _engine.SetSessionMute(s.Key, muted);
            if (s.Muted != muted)
            {
                group.SessionMap[s.Key] = s with { Muted = muted };
                changed = true;
            }
        }
        if (changed) GroupChanged?.Invoke(group);
    }

    /// <summary>Applies a level to one session only (used for sessions that appear while LOCK IN is on).</summary>
    public void SetSessionVolume(AppGroup group, string sessionKey, float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        if (!group.SessionMap.TryGetValue(sessionKey, out var s)) return;
        _engine.SetSessionVolume(sessionKey, volume);
        if (Math.Abs(s.Volume - volume) > 0.0005f)
        {
            group.SessionMap[sessionKey] = s with { Volume = volume };
            GroupChanged?.Invoke(group);
        }
    }

    public void SetSessionMute(AppGroup group, string sessionKey, bool muted)
    {
        if (!group.SessionMap.TryGetValue(sessionKey, out var s)) return;
        _engine.SetSessionMute(sessionKey, muted);
        if (s.Muted != muted)
        {
            group.SessionMap[sessionKey] = s with { Muted = muted };
            GroupChanged?.Invoke(group);
        }
    }

    public void SetMasterVolume(float volume)
    {
        volume = Math.Clamp(volume, 0f, 1f);
        _engine.SetMasterVolume(volume);
        if (Endpoint is { } e && Math.Abs(e.Volume - volume) > 0.0005f)
        {
            Endpoint = e with { Volume = volume };
            EndpointChanged?.Invoke();
        }
    }

    public void SetMasterMute(bool muted)
    {
        _engine.SetMasterMute(muted);
        if (Endpoint is { } e && e.Muted != muted)
        {
            Endpoint = e with { Muted = muted };
            EndpointChanged?.Invoke();
        }
    }

    // ---- engine callbacks (any thread) ----

    private void Post(Action action)
    {
        if (_disposed) return;
        if (_sync is null) action();
        else _sync.Post(_ => { if (!_disposed) action(); }, null);
    }

    private void OnDevices(IReadOnlyList<AudioDevice> devices) => Post(() =>
    {
        Devices = devices;
        DevicesChanged?.Invoke();
    });

    private void OnEndpoint(EndpointState? state) => Post(() =>
    {
        Endpoint = state;
        EndpointChanged?.Invoke();
    });

    private void OnStatus(EngineStatus status, string? message) => Post(() =>
    {
        Status = status;
        StatusMessage = message;
        StatusChanged?.Invoke();
    });

    private void OnInitialScan() => Post(() =>
    {
        IsReady = true;
        StatusChanged?.Invoke();
    });

    private void OnSessionUpdated(SessionUpdate update) => Post(() => ApplySessionUpdate(update));

    private void OnSessionRemoved(string sessionKey) => Post(() => ApplySessionRemoved(sessionKey));

    private void OnMeters(MeterFrame frame)
    {
        // Drop a frame instead of queueing when the UI thread has not consumed the previous one.
        if (Interlocked.Exchange(ref _meterPending, 1) == 1) return;
        Post(() =>
        {
            Interlocked.Exchange(ref _meterPending, 0);
            ApplyMeters(frame);
        });
    }

    // ---- state updates (UI thread) ----

    private void ApplySessionUpdate(SessionUpdate update)
    {
        var info = update.Info;
        if (info.State == SessionState.Expired)
        {
            ApplySessionRemoved(info.Key);
            return;
        }

        var identity = IdentityOf(info);
        if (_sessionToGroup.TryGetValue(info.Key, out var oldGroupKey) && oldGroupKey != identity.Key)
            ApplySessionRemoved(info.Key);

        var isNewGroup = !_groups.TryGetValue(identity.Key, out var group);
        if (isNewGroup)
        {
            group = new AppGroup(identity);
            _groups[identity.Key] = group;
        }

        var hadSession = group!.SessionMap.TryGetValue(info.Key, out var previous);
        group.SessionMap[info.Key] = info;
        _sessionToGroup[info.Key] = identity.Key;

        if (isNewGroup)
        {
            GroupAdded?.Invoke(group);
            return;
        }

        if (!hadSession)
        {
            GroupChanged?.Invoke(group);
            SessionAdded?.Invoke(group, info);
            return;
        }

        if (previous != info)
        {
            GroupChanged?.Invoke(group);
            var levelChanged = previous!.Muted != info.Muted || Math.Abs(previous.Volume - info.Volume) > 0.0005f;
            if (update.ExternalChange && levelChanged) ExternalChange?.Invoke(group);
        }
    }

    private void ApplySessionRemoved(string sessionKey)
    {
        if (!_sessionToGroup.Remove(sessionKey, out var groupKey)) return;
        if (!_groups.TryGetValue(groupKey, out var group)) return;
        group.SessionMap.Remove(sessionKey);
        if (group.SessionMap.Count == 0)
        {
            _groups.Remove(groupKey);
            GroupRemoved?.Invoke(group);
        }
        else
        {
            GroupChanged?.Invoke(group);
        }
    }

    private void ApplyMeters(MeterFrame frame)
    {
        MasterPeak = frame.Master;
        foreach (var g in _groups.Values) g.Peak = 0f;
        for (var i = 0; i < frame.Keys.Length; i++)
        {
            if (_sessionToGroup.TryGetValue(frame.Keys[i], out var gk) && _groups.TryGetValue(gk, out var g) && frame.Peaks[i] > g.Peak)
                g.Peak = frame.Peaks[i];
        }
        MetersUpdated?.Invoke();
    }

    internal static AppIdentity IdentityOf(SessionInfo s)
    {
        if (s.IsSystemSounds) return AppIdentity.SystemSounds;
        if (!string.IsNullOrWhiteSpace(s.ExePath)) return AppIdentity.FromPath(s.ExePath);
        if (!string.IsNullOrWhiteSpace(s.ProcessName)) return AppIdentity.FromProcessName(s.ProcessName);
        return new AppIdentity($"pid:{s.ProcessId}", $"pid {s.ProcessId}", null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.DevicesChanged -= OnDevices;
        _engine.EndpointChanged -= OnEndpoint;
        _engine.SessionUpdated -= OnSessionUpdated;
        _engine.SessionRemoved -= OnSessionRemoved;
        _engine.MetersUpdated -= OnMeters;
        _engine.StatusChanged -= OnStatus;
        _engine.InitialScanCompleted -= OnInitialScan;
    }
}
