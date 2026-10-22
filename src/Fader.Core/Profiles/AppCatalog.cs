using Fader.Core.Apps;
using Fader.Core.Settings;

namespace Fader.Core.Profiles;

/// <summary>Effective category of an app: the user's override if any, else the built-in classification.</summary>
public sealed class AppCatalog(SettingsService settings)
{
    public AppCategory Categorize(AppIdentity identity)
    {
        if (identity.IsSystemSounds) return AppCategory.System;
        var overrides = settings.Current.CategoryOverrides;
        if (overrides.TryGetValue(identity.Key, out var c)) return c;
        foreach (var (key, value) in overrides)
            if (key.Equals("name:" + identity.ExeName, StringComparison.Ordinal)) return value;
        return KnownApps.Classify(identity);
    }

    public bool IsOverridden(AppIdentity identity) => settings.Current.CategoryOverrides.ContainsKey(identity.Key);

    public void SetCategory(AppIdentity identity, AppCategory category)
    {
        if (identity.IsSystemSounds) return;
        var overrides = settings.Current.CategoryOverrides;
        if (category == KnownApps.Classify(identity)) overrides.Remove(identity.Key);
        else overrides[identity.Key] = category;
        settings.MarkDirty();
        Changed?.Invoke();
    }

    public event Action? Changed;
}
