using Fader.Core.Audio;
using Fader.Native;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Fader.Audio;

/// <summary>
/// One live <see cref="AudioSessionControl"/> plus the bookkeeping the engine needs: last known state,
/// recent writes made by FADER (to tell its own echoes from outside changes), and the COM event sink.
/// Created and used on the engine thread; COM callbacks only enqueue work back onto it.
/// </summary>
internal sealed class SessionEntry : IAudioSessionEventsHandler, IDisposable
{
    private static readonly long SelfWindowMs = 2000;

    private readonly WasapiAudioEngine _engine;
    private readonly List<(float Value, long At)> _volumeWrites = [];
    private readonly List<(bool Value, long At)> _muteWrites = [];
    private bool _registered;

    private SessionEntry(WasapiAudioEngine engine, DeviceMonitor monitor, AudioSessionControl control, string key,
        uint processId, string? exePath, string? processName, string? sessionName, bool isSystem)
    {
        _engine = engine;
        Monitor = monitor;
        Control = control;
        Key = key;
        ProcessId = processId;
        ExePath = exePath;
        ProcessName = processName;
        SessionName = sessionName;
        IsSystemSounds = isSystem;
    }

    public DeviceMonitor Monitor { get; }
    public AudioSessionControl Control { get; private set; }
    public string Key { get; }
    public uint ProcessId { get; }
    public string? ExePath { get; }
    public string? ProcessName { get; }
    public string? SessionName { get; private set; }
    public bool IsSystemSounds { get; }

    public Core.Audio.SessionState State { get; set; }
    public float Volume { get; set; }
    public bool Muted { get; set; }
    public bool Disposed { get; private set; }
    public AudioMeterInformation? Meter { get; private set; }

    public static SessionEntry? TryCreate(WasapiAudioEngine engine, DeviceMonitor monitor, AudioSessionControl control, string key)
    {
        try
        {
            var system = control.IsSystemSoundsSession;
            var pid = control.GetProcessID;
            var exePath = system ? null : ProcessInfo.TryGetImagePath(pid);
            var processName = system || exePath is not null ? null : ProcessInfo.TryGetProcessName(pid);
            var name = control.DisplayName;
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@')) name = null;

            var entry = new SessionEntry(engine, monitor, control, key, pid, exePath, processName, name, system);
            entry.ReadLevels();
            entry.State = MapState(control.State);
            entry.Meter = control.AudioMeterInformation;
            control.RegisterEventClient(entry);
            entry._registered = true;
            return entry;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static Core.Audio.SessionState MapState(AudioSessionState state) => state switch
    {
        AudioSessionState.AudioSessionStateActive => Core.Audio.SessionState.Active,
        AudioSessionState.AudioSessionStateExpired => Core.Audio.SessionState.Expired,
        _ => Core.Audio.SessionState.Inactive,
    };

    public SessionInfo ToInfo() => new(Key, Monitor.Id, ProcessId, ExePath, ProcessName, SessionName, IsSystemSounds, State, Volume, Muted);

    public void ReadLevels()
    {
        var volume = Control.SimpleAudioVolume;
        Volume = volume.Volume;
        Muted = volume.Mute;
    }

    // ---- writes made by FADER ----

    public void WriteVolume(float value)
    {
        var now = Environment.TickCount64;
        Trim(_volumeWrites, now);
        _volumeWrites.Add((value, now));
        Volume = value;
        Control.SimpleAudioVolume.Volume = value;
    }

    public void WriteMute(bool muted)
    {
        var now = Environment.TickCount64;
        Trim(_muteWrites, now);
        _muteWrites.Add((muted, now));
        Muted = muted;
        Control.SimpleAudioVolume.Mute = muted;
    }

    /// <summary>True if the reported level differs from what we know and FADER did not just write it.</summary>
    public bool IsExternalChange(float volume, bool muted)
    {
        var now = Environment.TickCount64;
        var volumeChanged = Math.Abs(volume - Volume) > 0.004f;
        var muteChanged = muted != Muted;
        var external = false;

        if (volumeChanged && !_volumeWrites.Exists(w => now - w.At <= SelfWindowMs && Math.Abs(w.Value - volume) < 0.006f))
            external = true;
        if (muteChanged && !_muteWrites.Exists(w => now - w.At <= SelfWindowMs && w.Value == muted))
            external = true;
        return external;
    }

    private static void Trim<T>(List<(T Value, long At)> list, long now)
    {
        list.RemoveAll(w => now - w.At > SelfWindowMs);
        if (list.Count > 64) list.RemoveRange(0, list.Count - 64);
    }

    // ---- IAudioSessionEventsHandler (COM threads: enqueue only) ----

    public void OnVolumeChanged(float volume, bool isMuted) =>
        _engine.Enqueue(() => _engine.HandleVolume(this, volume, isMuted));

    public void OnStateChanged(AudioSessionState state) =>
        _engine.Enqueue(() => _engine.HandleState(this, MapState(state)));

    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) =>
        _engine.Enqueue(() => _engine.HandleDisconnected(this, disconnectReason));

    public void OnDisplayNameChanged(string displayName) =>
        _engine.Enqueue(() => _engine.HandleDisplayName(this, displayName));

    public void OnIconPathChanged(string iconPath) { }

    public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }

    public void OnGroupingParamChanged(ref Guid groupingId) { }

    public void RenameSession(string? name) => SessionName = string.IsNullOrWhiteSpace(name) || name.StartsWith('@') ? null : name;

    public void Dispose()
    {
        if (Disposed) return;
        Disposed = true;
        try
        {
            if (_registered) Control.UnRegisterEventClient(this);
        }
        catch (Exception)
        {
            // The session may already be gone.
        }
        try { Control.Dispose(); } catch (Exception) { }
        _registered = false;
    }
}
