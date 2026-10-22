using Fader.Core.Audio;

namespace Fader.Tests.Support;

/// <summary>
/// An in-memory stand-in for Windows audio, used only by tests. Commands update its own state the way Windows
/// does (no echo for FADER's own writes); tests push "outside" changes and new sessions explicitly.
/// </summary>
public sealed class MockAudioEngine : IAudioEngine
{
    private readonly Dictionary<string, SessionInfo> _sessions = new();

    public const string Device = "dev1";

    public event Action<IReadOnlyList<AudioDevice>>? DevicesChanged;
    public event Action<EndpointState?>? EndpointChanged;
    public event Action<SessionUpdate>? SessionUpdated;
    public event Action<string>? SessionRemoved;
    public event Action<MeterFrame>? MetersUpdated;
    public event Action<EngineStatus, string?>? StatusChanged;
    public event Action? InitialScanCompleted;

    public List<(string Key, float Volume)> VolumeWrites { get; } = [];
    public List<(string Key, bool Muted)> MuteWrites { get; } = [];
    public float MasterVolume { get; private set; } = 1f;
    public bool MasterMuted { get; private set; }
    public OutputPreference? LastPreference { get; private set; }
    public bool MetersEnabled { get; private set; }
    public int Starts { get; private set; }

    public SessionInfo this[string key] => _sessions[key];

    public static SessionInfo Session(string key, string? exePath = null, float volume = 1f, bool muted = false,
        SessionState state = SessionState.Active, string device = Device, bool system = false, uint pid = 100, string? processName = null) =>
        new(key, device, pid, exePath, processName, null, system, state, volume, muted);

    public void Start(OutputPreference preference, bool includeOtherDevices)
    {
        Starts++;
        LastPreference = preference;
    }

    /// <summary>Raises the end of the first scan, like the real engine after it has listed existing sessions.</summary>
    public void CompleteInitialScan() => InitialScanCompleted?.Invoke();

    public void AddSession(SessionInfo session, bool external = false)
    {
        var isNew = !_sessions.ContainsKey(session.Key);
        _sessions[session.Key] = session;
        SessionUpdated?.Invoke(new SessionUpdate(session, isNew, external));
    }

    /// <summary>Another program (or the Windows mixer) changes a session's level.</summary>
    public void ExternalChange(string key, float? volume = null, bool? muted = null)
    {
        var s = _sessions[key];
        s = s with { Volume = volume ?? s.Volume, Muted = muted ?? s.Muted };
        _sessions[key] = s;
        SessionUpdated?.Invoke(new SessionUpdate(s, IsNew: false, ExternalChange: true));
    }

    public void ChangeState(string key, SessionState state)
    {
        var s = _sessions[key] with { State = state };
        _sessions[key] = s;
        SessionUpdated?.Invoke(new SessionUpdate(s, IsNew: false, ExternalChange: false));
    }

    public void RemoveSession(string key)
    {
        _sessions.Remove(key);
        SessionRemoved?.Invoke(key);
    }

    public void RaiseEndpoint(EndpointState? state) => EndpointChanged?.Invoke(state);
    public void RaiseDevices(IReadOnlyList<AudioDevice> devices) => DevicesChanged?.Invoke(devices);
    public void RaiseStatus(EngineStatus status, string? message = null) => StatusChanged?.Invoke(status, message);
    public void RaiseMeters(MeterFrame frame) => MetersUpdated?.Invoke(frame);

    public void SetOutputPreference(OutputPreference preference) => LastPreference = preference;
    public void SetIncludeOtherDevices(bool include) { }
    public void SetMetersEnabled(bool enabled) => MetersEnabled = enabled;
    public void SetMasterVolume(float volume) => MasterVolume = volume;
    public void SetMasterMute(bool muted) => MasterMuted = muted;

    public void SetSessionVolume(string sessionKey, float volume)
    {
        VolumeWrites.Add((sessionKey, volume));
        if (_sessions.TryGetValue(sessionKey, out var s)) _sessions[sessionKey] = s with { Volume = volume };
    }

    public void SetSessionMute(string sessionKey, bool muted)
    {
        MuteWrites.Add((sessionKey, muted));
        if (_sessions.TryGetValue(sessionKey, out var s)) _sessions[sessionKey] = s with { Muted = muted };
    }

    public void Refresh() { }

    public void Dispose() { }
}
