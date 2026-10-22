using Fader.Core.Apps;
using Fader.Core.Audio;

namespace Fader.Core.LockIn;

public enum RestorePolicy
{
    /// <summary>Turning LOCK IN off puts every app back where it was when LOCK IN started.</summary>
    RestorePrevious,
    /// <summary>Turning LOCK IN off leaves levels exactly as they are now.</summary>
    KeepCurrent,
    /// <summary>Turning LOCK IN off from the FADER window asks which of the two to do. Hotkeys and the tray restore.</summary>
    AskEachTime,
}

public enum DeactivateMode { RestorePrevious, KeepCurrent }

public enum SnapshotKind { LockIn, Profile }

public sealed class LockInSettings
{
    public string ProfileId { get; set; } = Profiles.DefaultProfiles.LockInId;
    public RestorePolicy RestorePolicy { get; set; } = RestorePolicy.RestorePrevious;

    /// <summary>
    /// When true, a level changed by something other than FADER (a game, Discord, the Windows mixer) is put back
    /// to the profile target while LOCK IN is on. Off by default so intentional manual changes stick.
    /// </summary>
    public bool EnforceProfile { get; set; }
}

public sealed class SnapshotEntry
{
    public AppIdentity App { get; set; } = AppIdentity.SystemSounds;
    public float Volume { get; set; }
    public bool Muted { get; set; }
}

/// <summary>The levels apps had before FADER changed them, so they can be put back.</summary>
public sealed class LevelSnapshot
{
    public SnapshotKind Kind { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<SnapshotEntry> Entries { get; set; } = [];

    public static LevelSnapshot Capture(IEnumerable<AppGroup> groups, SnapshotKind kind, DateTime nowUtc) => new()
    {
        Kind = kind,
        CreatedUtc = nowUtc,
        Entries = groups.Select(g => new SnapshotEntry { App = g.Identity, Volume = g.Volume, Muted = g.Muted }).ToList(),
    };

    public bool Contains(AppGroup group) => Entries.Any(e => e.App.Key == group.Key);
}

/// <summary>Persisted so LOCK IN survives a restart of FADER and can still be undone.</summary>
public sealed class LockInState
{
    public bool Active { get; set; }
    public LevelSnapshot? Snapshot { get; set; }

    /// <summary>The app treated as "current game" while LOCK IN is on.</summary>
    public string? GameKey { get; set; }
}

public readonly record struct ActionOutcome(bool Success, string Message, bool NeedsDecision = false);
