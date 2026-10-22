namespace Fader.Core.Audio;

public enum DeviceRole { Multimedia, Communications }

public enum OutputMode { FollowMultimedia, FollowCommunications, Specific }

public enum SessionState { Inactive, Active, Expired }

public enum EngineStatus { Starting, Running, NoDevice, Error }

public readonly record struct OutputPreference(OutputMode Mode, string? DeviceId);

public sealed record AudioDevice(string Id, string Name, bool IsDefaultMultimedia, bool IsDefaultCommunications);

/// <summary>The output endpoint whose master volume and sessions the mixer is showing.</summary>
public sealed record EndpointState(
    string DeviceId,
    string DeviceName,
    float Volume,
    bool Muted,
    bool IsFallback,
    bool IsDefaultMultimedia,
    bool IsDefaultCommunications);

/// <summary>One Windows audio session (one process stream on one endpoint).</summary>
public sealed record SessionInfo(
    string Key,
    string DeviceId,
    uint ProcessId,
    string? ExePath,
    string? ProcessName,
    string? SessionName,
    bool IsSystemSounds,
    SessionState State,
    float Volume,
    bool Muted);

/// <param name="Info">Current state of the session.</param>
/// <param name="IsNew">The session was not known before this update.</param>
/// <param name="ExternalChange">Volume or mute was changed by something other than FADER.</param>
public sealed record SessionUpdate(SessionInfo Info, bool IsNew, bool ExternalChange);

/// <summary>Peak levels (0..1) for the master endpoint and for each session, sampled at one instant.</summary>
public sealed class MeterFrame(float master, string[] keys, float[] peaks)
{
    public float Master { get; } = master;
    public string[] Keys { get; } = keys;
    public float[] Peaks { get; } = peaks;
}
