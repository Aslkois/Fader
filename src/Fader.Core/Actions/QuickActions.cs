using Fader.Core.Apps;
using Fader.Core.Audio;
using Fader.Core.LockIn;
using Fader.Core.Settings;

namespace Fader.Core.Actions;

/// <summary>
/// The actions behind hotkeys and tray items. Each returns a short message suitable for
/// on-screen feedback. UI-thread only.
/// </summary>
public sealed class QuickActions(MixerService mixer, SettingsService settings)
{
    public IReadOnlyList<AppGroup> ChatGroups()
    {
        var targets = settings.Current.ChatTargets;
        return mixer.Groups
            .Where(g => !g.Identity.IsSystemSounds && targets.Any(t => string.Equals(t, g.Identity.ExeName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>True when every running chat app is muted.</summary>
    public bool IsChatMuted()
    {
        var groups = ChatGroups();
        return groups.Count > 0 && groups.All(g => g.Muted);
    }

    public string ChatName()
    {
        var groups = ChatGroups();
        return groups.Count > 0
            ? AppDisplayNames.Resolve(groups[0].Identity)
            : AppDisplayNames.Fallback(AppIdentity.FromProcessName(settings.Current.ChatTargets[0]));
    }

    public ActionOutcome ToggleChatMute()
    {
        var groups = ChatGroups();
        if (groups.Count == 0) return new(false, $"{ChatName()} is not playing audio");
        var mute = !groups.All(g => g.Muted);
        foreach (var g in groups) mixer.SetGroupMute(g, mute);
        return new(true, $"{ChatName()} {(mute ? "muted" : "unmuted")}");
    }

    public ActionOutcome ChatVolume(int direction)
    {
        var groups = ChatGroups();
        if (groups.Count == 0) return new(false, $"{ChatName()} is not playing audio");
        var step = settings.Current.VolumeStepPercent;
        var shown = 0;
        foreach (var g in groups)
        {
            var current = (int)Math.Round(g.Volume * 100f);
            var next = Math.Clamp(current + direction * step, 0, 100);
            mixer.SetGroupVolume(g, next / 100f);
            if (direction > 0 && g.Muted) mixer.SetGroupMute(g, false);
            shown = next;
        }
        return new(true, $"{ChatName()} {shown}%");
    }
}
