using Fader.Core.Apps;
using Fader.Core.Audio;
using Fader.Core.Hotkeys;
using Fader.Core.LockIn;
using Fader.Core.Profiles;

namespace Fader.Core.Settings;

public enum ThemePreference { System, Light, Dark }

public sealed class PinnedApp
{
    public AppIdentity App { get; set; } = AppIdentity.SystemSounds;
    public string? DisplayName { get; set; }

    /// <summary>Last level seen while the app was running, shown dimmed while it is not.</summary>
    public int? LastVolumePercent { get; set; }
    public bool? LastMuted { get; set; }
}

public sealed class WindowSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 460;
    public double Height { get; set; } = 700;
}

public sealed class AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public ThemePreference Theme { get; set; } = ThemePreference.System;
    public bool CloseToTray { get; set; } = true;
    public bool CompactRows { get; set; }
    public bool ShowHotkeyFeedback { get; set; } = true;

    public OutputMode OutputMode { get; set; } = OutputMode.FollowMultimedia;
    public string? OutputDeviceId { get; set; }
    public bool ShowAppsOnOtherDevices { get; set; }

    /// <summary>Step for the chat-volume hotkeys and the mouse wheel, in percentage points.</summary>
    public int VolumeStepPercent { get; set; } = 5;

    /// <summary>Executable names the "chat" hotkeys act on.</summary>
    public List<string> ChatTargets { get; set; } = [.. KnownApps.DefaultChatTargets];

    public List<PinnedApp> PinnedApps { get; set; } = [];
    public Dictionary<string, AppCategory> CategoryOverrides { get; set; } = new();
    public List<HotkeyBinding> Hotkeys { get; set; } = HotkeyDefaults.Create();

    public string ActiveProfileId { get; set; } = DefaultProfiles.DefaultId;
    public List<Profile> Profiles { get; set; } = DefaultProfiles.Create();

    public LockInSettings LockIn { get; set; } = new();
    public LockInState State { get; set; } = new();
    public WindowSettings Window { get; set; } = new();

    /// <summary>Repairs anything a hand-edited or older file could have left inconsistent.</summary>
    public void Normalize()
    {
        Version = CurrentVersion;
        VolumeStepPercent = Math.Clamp(VolumeStepPercent, 1, 25);

        ChatTargets ??= [];
        ChatTargets = ChatTargets.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).Distinct().ToList();
        if (ChatTargets.Count == 0) ChatTargets = [.. KnownApps.DefaultChatTargets];

        PinnedApps = (PinnedApps ?? []).Where(p => p.App is not null).GroupBy(p => p.App.Key).Select(g => g.First()).ToList();
        CategoryOverrides ??= new();

        Profiles ??= [];
        var seen = new HashSet<string>();
        Profiles = Profiles.Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Id) && seen.Add(p.Id)).ToList();
        foreach (var builtIn in DefaultProfiles.Create().Where(p => p.BuiltIn))
        {
            var existing = Profiles.FirstOrDefault(p => p.Id == builtIn.Id);
            if (existing is null) Profiles.Insert(Math.Min(Profiles.Count, builtIn.Id == DefaultProfiles.DefaultId ? 0 : Profiles.Count), builtIn);
            else existing.BuiltIn = true;
        }
        foreach (var p in Profiles)
        {
            p.Name = string.IsNullOrWhiteSpace(p.Name) ? "Profile" : p.Name.Trim();
            p.Entries ??= [];
            p.Entries = p.Entries.Where(e => e is not null && (e.Kind == ProfileTargetKind.Category || e.App is not null)).ToList();
            foreach (var e in p.Entries) e.Volume = Math.Clamp(e.Volume, 0, 100);
        }
        if (Profiles.All(p => p.Id != ActiveProfileId)) ActiveProfileId = DefaultProfiles.DefaultId;

        LockIn ??= new();
        if (Profiles.All(p => p.Id != LockIn.ProfileId)) LockIn.ProfileId = DefaultProfiles.LockInId;

        State ??= new();
        if (State.Snapshot is null) State.Active = false;

        Window ??= new();
        Window.Width = Math.Clamp(double.IsFinite(Window.Width) ? Window.Width : 460, 340, 2000);
        Window.Height = Math.Clamp(double.IsFinite(Window.Height) ? Window.Height : 700, 300, 3000);

        NormalizeHotkeys();
    }

    private void NormalizeHotkeys()
    {
        var list = new List<HotkeyBinding>();
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            var found = (Hotkeys ?? []).FirstOrDefault(b => b is not null && b.Action == action);
            list.Add(found ?? HotkeyDefaults.For(action));
        }
        Hotkeys = list;
    }
}
