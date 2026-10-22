using Fader.Core.Apps;
using Fader.Core.Profiles;
using Fader.Tests.Support;

namespace Fader.Tests.Profiles;

public class ProfileResolverTests
{
    private static ResolverInput Input(string path, AppCategory category)
    {
        var id = AppIdentity.FromPath(path);
        return new ResolverInput(id.Key, id, category);
    }

    [Fact]
    public void Category_entry_applies_to_apps_in_that_category()
    {
        var profile = new Profile { Entries = [ProfileEntry.ForCategory(AppCategory.Chat, 20)] };

        var hit = ProfileResolver.ResolveOne(profile, Input(Fixture.Discord, AppCategory.Chat));

        Assert.Equal(20, hit!.Volume);
        Assert.Null(ProfileResolver.ResolveOne(profile, Input(Fixture.Spotify, AppCategory.Music)));
    }

    [Fact]
    public void App_entry_beats_category_even_when_listed_after_it()
    {
        var discord = AppIdentity.FromPath(Fixture.Discord);
        var profile = new Profile
        {
            Entries = [ProfileEntry.ForCategory(AppCategory.Chat, 20), ProfileEntry.ForApp(discord, "Discord", 80)],
        };

        Assert.Equal(80, ProfileResolver.ResolveOne(profile, Input(Fixture.Discord, AppCategory.Chat))!.Volume);
    }

    [Fact]
    public void First_matching_category_entry_wins()
    {
        var profile = new Profile
        {
            Entries = [ProfileEntry.ForCategory(AppCategory.Chat, 10), ProfileEntry.ForCategory(AppCategory.Chat, 90)],
        };

        Assert.Equal(10, ProfileResolver.ResolveOne(profile, Input(Fixture.Discord, AppCategory.Chat))!.Volume);
    }

    [Fact]
    public void App_entry_survives_a_move_to_a_new_versioned_folder()
    {
        var profile = new Profile
        {
            Entries = [ProfileEntry.ForApp(AppIdentity.FromPath(@"C:\x\Discord\app-1.0.1\Discord.exe"), null, 33)],
        };

        var hit = ProfileResolver.ResolveOne(profile, Input(@"C:\x\Discord\app-1.0.2\Discord.exe", AppCategory.Chat));

        Assert.Equal(33, hit!.Volume);
    }

    [Fact]
    public void Resolve_skips_apps_with_no_entry()
    {
        var profile = new Profile { Entries = [ProfileEntry.ForCategory(AppCategory.Music, 30)] };

        var targets = ProfileResolver.Resolve(profile,
        [
            Input(Fixture.Discord, AppCategory.Chat),
            Input(Fixture.Spotify, AppCategory.Music),
        ]);

        var only = Assert.Single(targets);
        Assert.Equal(AppIdentity.FromPath(Fixture.Spotify).Key, only.GroupKey);
    }
}

public class ProfileServiceTests
{
    [Fact]
    public void Defaults_include_the_four_shipped_profiles()
    {
        using var f = new Fixture();

        Assert.Equal(
            [DefaultProfiles.DefaultId, DefaultProfiles.GamingId, DefaultProfiles.LockInId, DefaultProfiles.CustomId],
            f.Profiles.Profiles.Select(p => p.Id).ToArray());
    }

    [Theory]
    [InlineData("  Late night  ", "Late night")]
    [InlineData("", "Profile")]
    [InlineData("   ", "Profile")]
    public void Create_cleans_the_name(string input, string expected)
    {
        using var f = new Fixture();
        Assert.Equal(expected, f.Profiles.Create(input).Name);
    }

    [Fact]
    public void Create_truncates_long_names()
    {
        using var f = new Fixture();
        var profile = f.Profiles.Create(new string('x', 100));
        Assert.Equal(ProfileService.MaxNameLength, profile.Name.Length);
    }

    [Fact]
    public void Create_can_copy_an_existing_profile_without_sharing_entries()
    {
        using var f = new Fixture();
        var source = f.Profiles.Get(DefaultProfiles.GamingId)!;

        var copy = f.Profiles.Create("Gaming 2", source);
        copy.Entries[0].Volume = 1;

        Assert.False(copy.BuiltIn);
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal(source.Entries.Count, copy.Entries.Count);
        Assert.NotEqual(1, source.Entries[0].Volume);
    }

    [Fact]
    public void Built_in_profiles_cannot_be_deleted()
    {
        using var f = new Fixture();
        Assert.False(f.Profiles.Delete(DefaultProfiles.GamingId));
        Assert.NotNull(f.Profiles.Get(DefaultProfiles.GamingId));
    }

    [Fact]
    public void Deleting_the_active_or_lock_in_profile_falls_back_to_the_defaults()
    {
        using var f = new Fixture();
        var mine = f.Profiles.Create("Mine");
        f.Settings.Current.ActiveProfileId = mine.Id;
        f.Settings.Current.LockIn.ProfileId = mine.Id;

        Assert.True(f.Profiles.Delete(mine.Id));

        Assert.Equal(DefaultProfiles.DefaultId, f.Settings.Current.ActiveProfileId);
        Assert.Equal(DefaultProfiles.LockInId, f.Settings.Current.LockIn.ProfileId);
    }

    [Fact]
    public void ResetBuiltIn_restores_shipped_values()
    {
        using var f = new Fixture();
        f.Profiles.SetCategoryEntry(DefaultProfiles.LockInId, AppCategory.Chat, 99);

        Assert.True(f.Profiles.ResetBuiltIn(DefaultProfiles.LockInId));

        var chat = f.Profiles.Get(DefaultProfiles.LockInId)!.Entries.Single(e => e.Category == AppCategory.Chat);
        Assert.Equal(20, chat.Volume);
        Assert.False(f.Profiles.ResetBuiltIn(DefaultProfiles.CustomId));
    }

    [Fact]
    public void SetAppEntry_updates_the_existing_entry_for_the_same_app()
    {
        using var f = new Fixture();
        var v1 = AppIdentity.FromPath(@"C:\x\Discord\app-1.0.1\Discord.exe");
        var v2 = AppIdentity.FromPath(@"C:\x\Discord\app-1.0.2\Discord.exe");

        f.Profiles.SetAppEntry(DefaultProfiles.CustomId, v1, "Discord", 50);
        f.Profiles.SetAppEntry(DefaultProfiles.CustomId, v2, null, 70, mute: true);

        var entry = Assert.Single(f.Profiles.Get(DefaultProfiles.CustomId)!.Entries);
        Assert.Equal(70, entry.Volume);
        Assert.True(entry.Mute);
        Assert.Equal("Discord", entry.Label);
    }

    [Fact]
    public void Entry_volumes_are_clamped_whether_new_or_updated()
    {
        using var f = new Fixture();
        var app = AppIdentity.FromPath(Fixture.Game);

        var created = f.Profiles.SetAppEntry(DefaultProfiles.CustomId, app, null, 250);
        Assert.Equal(100, created.Volume);

        var updated = f.Profiles.SetAppEntry(DefaultProfiles.CustomId, app, null, -5);
        Assert.Equal(0, updated.Volume);

        var category = f.Profiles.SetCategoryEntry(DefaultProfiles.CustomId, AppCategory.Music, 180);
        Assert.Equal(100, category.Volume);
    }

    [Fact]
    public void Unknown_profile_is_rejected()
    {
        using var f = new Fixture();
        Assert.Throws<ArgumentException>(() => f.Profiles.SetCategoryEntry("nope", AppCategory.Chat, 10));
        Assert.False(f.Profiles.Rename("nope", "x"));
    }

    [Fact]
    public void Remove_app_entry_finds_it_by_identity()
    {
        using var f = new Fixture();
        var app = AppIdentity.FromPath(Fixture.Game);
        f.Profiles.SetAppEntry(DefaultProfiles.CustomId, app, null, 40);

        Assert.NotNull(f.Profiles.FindAppEntry(DefaultProfiles.CustomId, app));
        Assert.True(f.Profiles.RemoveAppEntry(DefaultProfiles.CustomId, app));
        Assert.Null(f.Profiles.FindAppEntry(DefaultProfiles.CustomId, app));
        Assert.False(f.Profiles.RemoveAppEntry(DefaultProfiles.CustomId, app));
    }

    [Fact]
    public void Edits_raise_Changed()
    {
        using var f = new Fixture();
        var raised = 0;
        f.Profiles.Changed += () => raised++;

        var p = f.Profiles.Create("A");
        f.Profiles.Rename(p.Id, "B");
        f.Profiles.Rename(p.Id, "B");
        f.Profiles.Delete(p.Id);

        Assert.Equal(3, raised);
    }
}
