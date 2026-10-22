using Fader.Core.Apps;
using Fader.Core.Settings;

namespace Fader.Core.Profiles;

public sealed class ProfileService(SettingsService settings)
{
    public const int MaxNameLength = 40;

    public IReadOnlyList<Profile> Profiles => settings.Current.Profiles;

    public event Action? Changed;

    public Profile? Get(string? id) => id is null ? null : settings.Current.Profiles.FirstOrDefault(p => p.Id == id);

    public Profile? Active => Get(settings.Current.ActiveProfileId);

    public Profile Create(string name, Profile? copyFrom = null)
    {
        var clean = CleanName(name);
        var id = Guid.NewGuid().ToString("N");
        var profile = copyFrom is null
            ? new Profile { Id = id, Name = clean }
            : copyFrom.Clone(id, clean);
        settings.Current.Profiles.Add(profile);
        Touch();
        return profile;
    }

    public bool Rename(string id, string name)
    {
        var profile = Get(id);
        if (profile is null) return false;
        var clean = CleanName(name);
        if (clean == profile.Name) return true;
        profile.Name = clean;
        Touch();
        return true;
    }

    /// <summary>Removes a custom profile. Built-in profiles cannot be deleted.</summary>
    public bool Delete(string id)
    {
        var profile = Get(id);
        if (profile is null || profile.BuiltIn) return false;
        settings.Current.Profiles.Remove(profile);
        if (settings.Current.ActiveProfileId == id) settings.Current.ActiveProfileId = DefaultProfiles.DefaultId;
        if (settings.Current.LockIn.ProfileId == id) settings.Current.LockIn.ProfileId = DefaultProfiles.LockInId;
        Touch();
        return true;
    }

    /// <summary>Restores a built-in profile's shipped values.</summary>
    public bool ResetBuiltIn(string id)
    {
        var profile = Get(id);
        if (profile is null || !profile.BuiltIn) return false;
        profile.Entries = DefaultProfiles.CreateBuiltIn(id).Entries;
        Touch();
        return true;
    }

    public ProfileEntry SetAppEntry(string profileId, AppIdentity app, string? label, int volume, bool mute = false)
    {
        var profile = Get(profileId) ?? throw new ArgumentException("Unknown profile", nameof(profileId));
        var existing = profile.Entries.FirstOrDefault(e => e.Kind == ProfileTargetKind.App && e.App is not null && e.App.Matches(app));
        if (existing is null)
        {
            existing = ProfileEntry.ForApp(app, label, volume, mute);
            profile.Entries.Add(existing);
        }
        else
        {
            existing.Volume = Math.Clamp(volume, 0, 100);
            existing.Mute = mute;
            existing.Label = label ?? existing.Label;
        }
        Touch();
        return existing;
    }

    public ProfileEntry SetCategoryEntry(string profileId, AppCategory category, int volume, bool mute = false)
    {
        var profile = Get(profileId) ?? throw new ArgumentException("Unknown profile", nameof(profileId));
        var existing = profile.Entries.FirstOrDefault(e => e.Kind == ProfileTargetKind.Category && e.Category == category);
        if (existing is null)
        {
            existing = ProfileEntry.ForCategory(category, volume, mute);
            profile.Entries.Add(existing);
        }
        else
        {
            existing.Volume = Math.Clamp(volume, 0, 100);
            existing.Mute = mute;
        }
        Touch();
        return existing;
    }

    public bool RemoveEntry(string profileId, ProfileEntry entry)
    {
        var profile = Get(profileId);
        if (profile is null || !profile.Entries.Remove(entry)) return false;
        Touch();
        return true;
    }

    public bool RemoveAppEntry(string profileId, AppIdentity app)
    {
        var profile = Get(profileId);
        var entry = profile?.Entries.FirstOrDefault(e => e.Kind == ProfileTargetKind.App && e.App is not null && e.App.Matches(app));
        return entry is not null && RemoveEntry(profileId, entry);
    }

    public ProfileEntry? FindAppEntry(string profileId, AppIdentity app) =>
        Get(profileId)?.Entries.FirstOrDefault(e => e.Kind == ProfileTargetKind.App && e.App is not null && e.App.Matches(app));

    /// <summary>Call after editing a profile's entries in place.</summary>
    public void Touch()
    {
        settings.MarkDirty();
        Changed?.Invoke();
    }

    private static string CleanName(string name)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0) clean = "Profile";
        return clean.Length > MaxNameLength ? clean[..MaxNameLength] : clean;
    }
}
