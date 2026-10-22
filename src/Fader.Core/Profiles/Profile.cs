using Fader.Core.Apps;

namespace Fader.Core.Profiles;

public enum ProfileTargetKind { Category, App }

/// <summary>
/// One line of a profile: a volume for either a whole <see cref="AppCategory"/> ("Chat apps")
/// or one specific application. A specific application always wins over a category.
/// </summary>
public sealed class ProfileEntry
{
    public ProfileTargetKind Kind { get; set; } = ProfileTargetKind.Category;
    public AppCategory Category { get; set; } = AppCategory.Other;
    public AppIdentity? App { get; set; }

    /// <summary>Last known name of the app, so the entry still reads well when the app is not running.</summary>
    public string? Label { get; set; }

    public int Volume { get; set; } = 100;

    /// <summary>When true the target is also muted. When false its mute state is left alone.</summary>
    public bool Mute { get; set; }

    public static ProfileEntry ForCategory(AppCategory category, int volume, bool mute = false) =>
        new() { Kind = ProfileTargetKind.Category, Category = category, Volume = Math.Clamp(volume, 0, 100), Mute = mute };

    public static ProfileEntry ForApp(AppIdentity app, string? label, int volume, bool mute = false) =>
        new() { Kind = ProfileTargetKind.App, App = app, Label = label, Volume = Math.Clamp(volume, 0, 100), Mute = mute };

    public string DisplayName => Kind == ProfileTargetKind.Category
        ? KnownApps.CategoryLabel(Category)
        : Label ?? (App is null ? "Unknown app" : AppDisplayNames.Fallback(App));
}

public sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Profile";

    /// <summary>Built-in profiles can be edited but not deleted.</summary>
    public bool BuiltIn { get; set; }

    public List<ProfileEntry> Entries { get; set; } = [];

    public Profile Clone(string newId, string newName) => new()
    {
        Id = newId,
        Name = newName,
        BuiltIn = false,
        Entries = Entries.Select(e => new ProfileEntry
        {
            Kind = e.Kind,
            Category = e.Category,
            App = e.App,
            Label = e.Label,
            Volume = e.Volume,
            Mute = e.Mute,
        }).ToList(),
    };
}

public static class DefaultProfiles
{
    public const string DefaultId = "default";
    public const string GamingId = "gaming";
    public const string LockInId = "lockin";
    public const string CustomId = "custom";

    public static List<Profile> Create() =>
    [
        new Profile
        {
            Id = DefaultId, Name = "Default", BuiltIn = true,
            Entries =
            [
                ProfileEntry.ForCategory(AppCategory.Game, 100),
                ProfileEntry.ForCategory(AppCategory.Chat, 100),
                ProfileEntry.ForCategory(AppCategory.Music, 100),
                ProfileEntry.ForCategory(AppCategory.Browser, 100),
            ],
        },
        new Profile
        {
            Id = GamingId, Name = "Gaming", BuiltIn = true,
            Entries =
            [
                ProfileEntry.ForCategory(AppCategory.Game, 100),
                ProfileEntry.ForCategory(AppCategory.Chat, 60),
                ProfileEntry.ForCategory(AppCategory.Music, 30),
                ProfileEntry.ForCategory(AppCategory.Browser, 40),
            ],
        },
        new Profile
        {
            Id = LockInId, Name = "LOCK IN", BuiltIn = true,
            Entries =
            [
                ProfileEntry.ForCategory(AppCategory.Game, 100),
                ProfileEntry.ForCategory(AppCategory.Chat, 20),
                ProfileEntry.ForCategory(AppCategory.Music, 30),
                ProfileEntry.ForCategory(AppCategory.Browser, 15),
            ],
        },
        new Profile { Id = CustomId, Name = "Custom", BuiltIn = false, Entries = [] },
    ];

    public static Profile CreateBuiltIn(string id) => Create().First(p => p.Id == id);
}
