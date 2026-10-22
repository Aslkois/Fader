using Fader.Core.Apps;

namespace Fader.Core.Audio;

/// <summary>All audio sessions that belong to one application, presented as one logical mixer row.</summary>
public sealed class AppGroup(AppIdentity identity)
{
    internal readonly Dictionary<string, SessionInfo> SessionMap = new();

    public AppIdentity Identity { get; } = identity;
    public string Key => Identity.Key;
    public IReadOnlyCollection<SessionInfo> Sessions => SessionMap.Values;

    /// <summary>Latest peak (0..1) across the group's sessions; written by the meter pump.</summary>
    public float Peak { get; internal set; }

    /// <summary>Highest session volume, so a quiet duplicate never hides an audible stream.</summary>
    public float Volume
    {
        get
        {
            var v = 0f;
            foreach (var s in SessionMap.Values) if (s.Volume > v) v = s.Volume;
            return v;
        }
    }

    public bool Muted
    {
        get
        {
            if (SessionMap.Count == 0) return false;
            foreach (var s in SessionMap.Values) if (!s.Muted) return false;
            return true;
        }
    }

    public bool IsPlaying
    {
        get
        {
            foreach (var s in SessionMap.Values) if (s.State == SessionState.Active) return true;
            return false;
        }
    }

    public string? SessionName
    {
        get
        {
            foreach (var s in SessionMap.Values) if (!string.IsNullOrWhiteSpace(s.SessionName)) return s.SessionName;
            return null;
        }
    }

    public bool IsOnDevice(string? deviceId)
    {
        foreach (var s in SessionMap.Values) if (s.DeviceId == deviceId) return true;
        return false;
    }
}
