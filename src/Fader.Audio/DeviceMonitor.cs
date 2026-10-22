using NAudio.CoreAudioApi;

namespace Fader.Audio;

/// <summary>An open render endpoint whose audio sessions are being tracked.</summary>
internal sealed class DeviceMonitor : IDisposable
{
    private readonly AudioSessionManager.SessionCreatedDelegate? _createdHandler;

    public DeviceMonitor(string id, MMDevice device, AudioSessionManager manager, AudioSessionManager.SessionCreatedDelegate createdHandler)
    {
        Id = id;
        Device = device;
        Manager = manager;
        _createdHandler = createdHandler;
        manager.OnSessionCreated += createdHandler;
    }

    public string Id { get; }
    public MMDevice Device { get; }
    public AudioSessionManager Manager { get; }
    public HashSet<string> SessionKeys { get; } = new(StringComparer.Ordinal);

    public void Dispose()
    {
        try
        {
            if (_createdHandler is not null) Manager.OnSessionCreated -= _createdHandler;
        }
        catch (Exception)
        {
        }
        try { Manager.Dispose(); } catch (Exception) { }
        try { Device.Dispose(); } catch (Exception) { }
    }
}
