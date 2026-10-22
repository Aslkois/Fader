namespace Fader.Core.Audio;

/// <summary>
/// The Windows audio backend. Events may be raised on any thread; consumers marshal as needed
/// (see <see cref="MixerService"/>). Commands are fire-and-forget and never block the caller on COM.
/// </summary>
public interface IAudioEngine : IDisposable
{
    event Action<IReadOnlyList<AudioDevice>>? DevicesChanged;
    event Action<EndpointState?>? EndpointChanged;
    event Action<SessionUpdate>? SessionUpdated;
    event Action<string>? SessionRemoved;
    event Action<MeterFrame>? MetersUpdated;
    event Action<EngineStatus, string?>? StatusChanged;
    event Action? InitialScanCompleted;

    void Start(OutputPreference preference, bool includeOtherDevices);
    void SetOutputPreference(OutputPreference preference);
    void SetIncludeOtherDevices(bool include);
    void SetMetersEnabled(bool enabled);
    void SetMasterVolume(float volume);
    void SetMasterMute(bool muted);
    void SetSessionVolume(string sessionKey, float volume);
    void SetSessionMute(string sessionKey, bool muted);
    void Refresh();
}
