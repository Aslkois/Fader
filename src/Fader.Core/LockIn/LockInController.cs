using Fader.Core.Apps;
using Fader.Core.Audio;
using Fader.Core.Profiles;
using Fader.Core.Settings;

namespace Fader.Core.LockIn;

/// <summary>
/// LOCK IN and profile application. Owns the "previous levels" snapshot and the rules for when
/// profile targets are (and are not) pushed onto audio sessions. UI-thread only.
/// </summary>
public sealed class LockInController : IDisposable
{
    private const int EnforceBurst = 3;
    private static readonly TimeSpan EnforceWindow = TimeSpan.FromSeconds(5);

    private readonly MixerService _mixer;
    private readonly ProfileService _profiles;
    private readonly AppCatalog _catalog;
    private readonly SettingsService _settings;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, Queue<long>> _enforceLog = new();

    public LockInController(MixerService mixer, ProfileService profiles, AppCatalog catalog, SettingsService settings, TimeProvider? time = null)
    {
        _mixer = mixer;
        _profiles = profiles;
        _catalog = catalog;
        _settings = settings;
        _time = time ?? TimeProvider.System;
        _mixer.GroupAdded += OnGroupAdded;
        _mixer.SessionAdded += OnSessionAdded;
        _mixer.ExternalChange += OnExternalChange;
    }

    /// <summary>Asked when a new app starts making sound during LOCK IN and no game is chosen yet.</summary>
    public Func<AppIdentity?>? ForegroundProbe { get; set; }

    public event Action? Changed;

    private LockInState State => _settings.Current.State;
    private LockInSettings Options => _settings.Current.LockIn;

    public bool IsActive => State.Active;
    public LevelSnapshot? Snapshot => State.Snapshot;
    public bool HasSnapshot => State.Snapshot is { Entries.Count: > 0 };
    public string? CurrentGameKey => State.GameKey;

    /// <summary>The category an app counts as right now, including the temporary "current game" role.</summary>
    public AppCategory EffectiveCategory(AppGroup group)
    {
        var category = _catalog.Categorize(group.Identity);
        if (category == AppCategory.Other && State.GameKey is { } key && key == group.Key) return AppCategory.Game;
        return category;
    }

    public ResolverInput InputFor(AppGroup group) => new(group.Key, group.Identity, EffectiveCategory(group));

    public ActionOutcome Toggle(AppIdentity? foreground = null, bool interactive = false)
    {
        if (!IsActive) return Activate(foreground);
        return Options.RestorePolicy switch
        {
            RestorePolicy.KeepCurrent => Deactivate(DeactivateMode.KeepCurrent),
            RestorePolicy.AskEachTime when interactive => new ActionOutcome(true, "Restore previous levels?", NeedsDecision: true),
            _ => Deactivate(DeactivateMode.RestorePrevious),
        };
    }

    public ActionOutcome Activate(AppIdentity? foreground = null)
    {
        if (IsActive) return new(true, "LOCK IN is already on");
        if (!_mixer.IsReady) return new(false, "Audio is still starting");
        var profile = _profiles.Get(Options.ProfileId) ?? _profiles.Get(DefaultProfiles.LockInId);
        if (profile is null) return new(false, "LOCK IN profile is missing");

        State.GameKey = DetectGame(foreground);
        State.Snapshot = LevelSnapshot.Capture(_mixer.Groups, SnapshotKind.LockIn, _time.GetUtcNow().UtcDateTime);
        State.Active = true;
        _enforceLog.Clear();

        var applied = ApplyTargets(profile);
        Save();
        return new(true, applied == 0
            ? "LOCK IN on. No running app matches the profile yet"
            : $"LOCK IN on. {applied} {(applied == 1 ? "app" : "apps")} set");
    }

    public ActionOutcome Deactivate(DeactivateMode mode)
    {
        if (!IsActive) return new(true, "LOCK IN is already off");
        var message = "LOCK IN off";
        if (mode == DeactivateMode.RestorePrevious && State.Snapshot is { } snapshot)
        {
            var restored = Restore(snapshot);
            message = restored == 0 ? "LOCK IN off. Nothing to restore" : $"LOCK IN off. Restored {restored} {(restored == 1 ? "app" : "apps")}";
        }
        else if (mode == DeactivateMode.KeepCurrent)
        {
            message = "LOCK IN off. Levels kept";
        }
        ClearLockIn();
        Save();
        return new(true, message);
    }

    /// <summary>Puts apps back to the levels saved before LOCK IN (or the last profile) changed them.</summary>
    public ActionOutcome RestorePrevious()
    {
        if (State.Snapshot is not { Entries.Count: > 0 } snapshot) return new(false, "Nothing to restore");
        var restored = Restore(snapshot);
        ClearLockIn();
        State.Snapshot = null;
        Save();
        return new(true, restored == 0 ? "None of the saved apps are running" : $"Restored {restored} {(restored == 1 ? "app" : "apps")}");
    }

    /// <summary>Applies a profile once. The levels before the change are kept so they can be restored.</summary>
    public ActionOutcome ApplyProfile(string profileId, AppIdentity? foreground = null)
    {
        var profile = _profiles.Get(profileId);
        if (profile is null) return new(false, "Profile not found");
        if (!_mixer.IsReady) return new(false, "Audio is still starting");

        _settings.Current.ActiveProfileId = profile.Id;
        if (!IsActive)
        {
            State.GameKey = DetectGame(foreground);
            State.Snapshot = LevelSnapshot.Capture(_mixer.Groups, SnapshotKind.Profile, _time.GetUtcNow().UtcDateTime);
        }
        var applied = ApplyTargets(profile);
        if (!IsActive) State.GameKey = null;
        Save();
        return new(true, applied == 0
            ? $"{profile.Name}: no running app matches"
            : $"{profile.Name}: {applied} {(applied == 1 ? "app" : "apps")} set");
    }

    /// <summary>
    /// "Current game" is the foreground app when LOCK IN starts, if it has an audio session and is not
    /// already classified as chat, music or a browser. When there is no usable foreground app (LOCK IN
    /// was started from the FADER window or tray), it is the single unclassified app that is currently
    /// playing sound, if there is exactly one. Returns its group key.
    /// </summary>
    private string? DetectGame(AppIdentity? foreground)
    {
        if (foreground is not null)
        {
            var group = _mixer.FindGroup(foreground);
            if (group is not null && _catalog.Categorize(group.Identity) == AppCategory.Other) return group.Key;
        }

        string? only = null;
        foreach (var g in _mixer.Groups)
        {
            if (!g.IsPlaying || _catalog.Categorize(g.Identity) != AppCategory.Other) continue;
            if (only is not null) return null;
            only = g.Key;
        }
        return only;
    }

    private int ApplyTargets(Profile profile)
    {
        var targets = ProfileResolver.Resolve(profile, _mixer.Groups.Select(InputFor).ToList());
        var applied = 0;
        foreach (var t in targets)
        {
            if (!_mixer.TryGetGroup(t.GroupKey, out var group)) continue;
            Apply(group, t);
            applied++;
        }
        return applied;
    }

    private void Apply(AppGroup group, ResolvedTarget target)
    {
        _mixer.SetGroupVolume(group, target.Volume / 100f);
        if (target.Mute) _mixer.SetGroupMute(group, true);
    }

    private int Restore(LevelSnapshot snapshot)
    {
        var restored = 0;
        foreach (var entry in snapshot.Entries)
        {
            var group = _mixer.FindGroup(entry.App);
            if (group is null) continue;
            _mixer.SetGroupVolume(group, entry.Volume);
            _mixer.SetGroupMute(group, entry.Muted);
            restored++;
        }
        return restored;
    }

    private void ClearLockIn()
    {
        State.Active = false;
        State.GameKey = null;
        State.Snapshot = null;
        _enforceLog.Clear();
    }

    private void Save()
    {
        _settings.MarkDirty();
        Changed?.Invoke();
    }

    // ---- sessions that appear or drift while LOCK IN is on ----

    private void OnGroupAdded(AppGroup group)
    {
        if (!IsActive || !_mixer.IsReady) return;
        AdoptGameIfUnset(group);
        RememberBeforeChange(group);
        var profile = ActiveLockInProfile();
        var target = profile is null ? null : ProfileResolver.ResolveOne(profile, InputFor(group));
        if (target is not null) Apply(group, target);
    }

    private void OnSessionAdded(AppGroup group, SessionInfo session)
    {
        if (!IsActive || !_mixer.IsReady) return;
        RememberBeforeChange(group);
        var profile = ActiveLockInProfile();
        var target = profile is null ? null : ProfileResolver.ResolveOne(profile, InputFor(group));
        if (target is null) return;
        _mixer.SetSessionVolume(group, session.Key, target.Volume / 100f);
        if (target.Mute) _mixer.SetSessionMute(group, session.Key, true);
    }

    private void OnExternalChange(AppGroup group)
    {
        if (!IsActive || !Options.EnforceProfile || !_mixer.IsReady) return;
        var profile = ActiveLockInProfile();
        var target = profile is null ? null : ProfileResolver.ResolveOne(profile, InputFor(group));
        if (target is null) return;

        var onTarget = Math.Abs(group.Volume * 100f - target.Volume) < 1f && (!target.Mute || group.Muted);
        if (onTarget || !AllowEnforcement(group.Key)) return;
        Apply(group, target);
    }

    private void AdoptGameIfUnset(AppGroup group)
    {
        if (State.GameKey is not null) return;
        if (_catalog.Categorize(group.Identity) != AppCategory.Other) return;
        var foreground = ForegroundProbe?.Invoke();
        if (foreground is not null && group.Identity.Matches(foreground))
        {
            State.GameKey = group.Key;
            Save();
        }
    }

    private void RememberBeforeChange(AppGroup group)
    {
        var snapshot = State.Snapshot;
        if (snapshot is null || snapshot.Contains(group)) return;
        snapshot.Entries.Add(new SnapshotEntry { App = group.Identity, Volume = group.Volume, Muted = group.Muted });
        _settings.MarkDirty();
    }

    private Profile? ActiveLockInProfile() => _profiles.Get(Options.ProfileId) ?? _profiles.Get(DefaultProfiles.LockInId);

    /// <summary>Allows a few corrections, then backs off so FADER never fights an app in a tight loop.</summary>
    private bool AllowEnforcement(string key)
    {
        var now = _time.GetTimestamp();
        if (!_enforceLog.TryGetValue(key, out var log)) _enforceLog[key] = log = new Queue<long>();
        while (log.Count > 0 && _time.GetElapsedTime(log.Peek(), now) > EnforceWindow) log.Dequeue();
        if (log.Count >= EnforceBurst) return false;
        log.Enqueue(now);
        return true;
    }

    public void Dispose()
    {
        _mixer.GroupAdded -= OnGroupAdded;
        _mixer.SessionAdded -= OnSessionAdded;
        _mixer.ExternalChange -= OnExternalChange;
    }
}
