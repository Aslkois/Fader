using Fader.Core.Apps;
using Fader.Core.Hotkeys;
using Fader.Core.LockIn;
using Fader.Core.Profiles;
using Fader.Core.Settings;

namespace Fader.Tests.Settings;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fader-tests-" + Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public SettingsService Open()
    {
        var s = new SettingsService(Path, TimeSpan.FromHours(1));
        s.Load();
        return s;
    }

    /// <summary>Writes two generations so both settings.json and settings.json.bak exist.</summary>
    public void SaveTwice(int first, int second)
    {
        using var s = Open();
        s.Current.VolumeStepPercent = first;
        s.MarkDirty();
        s.Flush();
        s.Current.VolumeStepPercent = second;
        s.MarkDirty();
        s.Flush();
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

public class SettingsServiceTests
{
    [Fact]
    public void First_run_uses_defaults_and_writes_nothing()
    {
        using var dir = new TempDir();
        using var s = dir.Open();

        Assert.Null(s.LoadNotice);
        Assert.Equal(5, s.Current.VolumeStepPercent);
        Assert.False(System.IO.File.Exists(s.FilePath));
    }

    [Fact]
    public void Writes_are_debounced_until_flushed()
    {
        using var dir = new TempDir();
        using var s = dir.Open();

        s.MarkDirty();
        Assert.False(System.IO.File.Exists(s.FilePath));

        s.Flush();
        Assert.True(System.IO.File.Exists(s.FilePath));
    }

    [Fact]
    public void Dispose_flushes_pending_changes()
    {
        using var dir = new TempDir();
        var s = dir.Open();
        s.Current.VolumeStepPercent = 11;
        s.MarkDirty();
        s.Dispose();

        using var again = dir.Open();
        Assert.Equal(11, again.Current.VolumeStepPercent);
    }

    [Fact]
    public void Settings_round_trip_through_json()
    {
        using var dir = new TempDir();
        using (var s = dir.Open())
        {
            s.Current.Theme = ThemePreference.Dark;
            s.Current.CompactRows = true;
            s.Current.CategoryOverrides["name:shooter.exe"] = AppCategory.Game;
            s.Current.PinnedApps.Add(new PinnedApp { App = AppIdentity.FromPath(@"C:\a\b.exe"), DisplayName = "B", LastVolumePercent = 42 });
            s.Current.Hotkeys.Single(h => h.Action == HotkeyAction.ToggleLockIn).Key = 0x58;
            s.Current.Profiles.Single(p => p.Id == DefaultProfiles.CustomId)
                .Entries.Add(ProfileEntry.ForApp(AppIdentity.FromPath(@"C:\g\game.exe"), "Game", 75, mute: true));
            s.MarkDirty();
        }

        using var loaded = dir.Open();
        var c = loaded.Current;
        Assert.Equal(ThemePreference.Dark, c.Theme);
        Assert.True(c.CompactRows);
        Assert.Equal(AppCategory.Game, c.CategoryOverrides["name:shooter.exe"]);
        Assert.Equal(42, c.PinnedApps.Single().LastVolumePercent);
        Assert.Equal(0x58u, c.Hotkeys.Single(h => h.Action == HotkeyAction.ToggleLockIn).Key);
        var entry = c.Profiles.Single(p => p.Id == DefaultProfiles.CustomId).Entries.Single();
        Assert.Equal(75, entry.Volume);
        Assert.True(entry.Mute);
        Assert.True(entry.App!.Matches(AppIdentity.FromPath(@"C:\g\game.exe")));
    }

    [Fact]
    public void Enums_are_stored_as_readable_names()
    {
        using var dir = new TempDir();
        using (var s = dir.Open())
        {
            s.Current.Theme = ThemePreference.Light;
            s.MarkDirty();
        }

        Assert.Contains("\"Light\"", System.IO.File.ReadAllText(dir.File("settings.json")));
    }

    [Fact]
    public void A_corrupt_file_is_replaced_by_the_backup()
    {
        using var dir = new TempDir();
        dir.SaveTwice(7, 9);
        System.IO.File.WriteAllText(dir.File("settings.json"), "{ not json");

        using var s = dir.Open();

        Assert.Equal(7, s.Current.VolumeStepPercent);
        Assert.Contains("restored", s.LoadNotice);
        Assert.Single(System.IO.Directory.GetFiles(dir.Path, "settings.json.corrupt-*"));
    }

    [Fact]
    public void A_corrupt_file_without_a_backup_resets_to_defaults_and_keeps_the_bad_file()
    {
        using var dir = new TempDir();
        System.IO.Directory.CreateDirectory(dir.Path);
        System.IO.File.WriteAllText(dir.File("settings.json"), "garbage");

        using var s = dir.Open();

        Assert.Equal(5, s.Current.VolumeStepPercent);
        Assert.Contains("kept the old file", s.LoadNotice);
        Assert.Single(System.IO.Directory.GetFiles(dir.Path, "settings.json.corrupt-*"));
    }

    [Fact]
    public void A_missing_file_with_a_backup_is_recovered()
    {
        using var dir = new TempDir();
        dir.SaveTwice(7, 9);
        System.IO.File.Delete(dir.File("settings.json"));

        using var s = dir.Open();

        Assert.Equal(7, s.Current.VolumeStepPercent);
        Assert.NotNull(s.LoadNotice);
    }

    [Fact]
    public void Repairs_are_saved_back()
    {
        using var dir = new TempDir();
        dir.SaveTwice(7, 9);
        System.IO.File.WriteAllText(dir.File("settings.json"), "{");

        using (dir.Open()) { }

        Assert.Contains("\"VolumeStepPercent\": 7", System.IO.File.ReadAllText(dir.File("settings.json")));
    }

    [Fact]
    public void A_failed_save_is_reported_and_retried()
    {
        using var dir = new TempDir();
        using var s = dir.Open();
        System.IO.Directory.CreateDirectory(dir.Path);
        using var blocker = new FileStream(dir.File("settings.json.tmp"), FileMode.Create, FileAccess.Write, FileShare.None);

        s.MarkDirty();
        s.Flush();
        Assert.NotNull(s.LastSaveError);

        blocker.Dispose();
        s.Flush();
        Assert.Null(s.LastSaveError);
        Assert.True(System.IO.File.Exists(s.FilePath));
    }
}

public class AppSettingsNormalizeTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(99, 25)]
    [InlineData(8, 8)]
    public void Volume_step_is_clamped(int input, int expected)
    {
        var s = new AppSettings { VolumeStepPercent = input };
        s.Normalize();
        Assert.Equal(expected, s.VolumeStepPercent);
    }

    [Fact]
    public void Chat_targets_are_cleaned_and_never_empty()
    {
        var s = new AppSettings { ChatTargets = [" Discord.EXE ", "discord.exe", "", "  "] };
        s.Normalize();
        Assert.Equal(["discord.exe"], s.ChatTargets);

        s.ChatTargets = [" "];
        s.Normalize();
        Assert.Equal(KnownApps.DefaultChatTargets, s.ChatTargets);
    }

    [Fact]
    public void Missing_built_in_profiles_come_back()
    {
        var s = new AppSettings { Profiles = [new Profile { Id = "mine", Name = "Mine" }] };
        s.Normalize();

        foreach (var id in new[] { DefaultProfiles.DefaultId, DefaultProfiles.GamingId, DefaultProfiles.LockInId })
            Assert.True(s.Profiles.Single(p => p.Id == id).BuiltIn, id);
        Assert.Contains(s.Profiles, p => p.Id == "mine");
    }

    [Fact]
    public void Duplicate_and_blank_profile_ids_are_dropped()
    {
        var s = new AppSettings
        {
            Profiles =
            [
                new Profile { Id = "a", Name = "First" },
                new Profile { Id = "a", Name = "Second" },
                new Profile { Id = " ", Name = "Blank" },
            ],
        };
        s.Normalize();

        Assert.Equal("First", s.Profiles.Single(p => p.Id == "a").Name);
        Assert.DoesNotContain(s.Profiles, p => string.IsNullOrWhiteSpace(p.Id));
    }

    [Fact]
    public void Dangling_profile_references_fall_back()
    {
        var s = new AppSettings { ActiveProfileId = "gone" };
        s.LockIn.ProfileId = "also-gone";
        s.Normalize();

        Assert.Equal(DefaultProfiles.DefaultId, s.ActiveProfileId);
        Assert.Equal(DefaultProfiles.LockInId, s.LockIn.ProfileId);
    }

    [Fact]
    public void Profile_entries_are_validated()
    {
        var custom = new Profile
        {
            Id = DefaultProfiles.CustomId,
            Name = "  ",
            Entries =
            [
                ProfileEntry.ForCategory(AppCategory.Chat, 400),
                new ProfileEntry { Kind = ProfileTargetKind.App, App = null },
            ],
        };
        var s = new AppSettings { Profiles = [custom] };
        s.Normalize();

        var fixedUp = s.Profiles.Single(p => p.Id == DefaultProfiles.CustomId);
        Assert.Equal("Profile", fixedUp.Name);
        Assert.Equal(100, Assert.Single(fixedUp.Entries).Volume);
    }

    [Fact]
    public void Lock_in_without_a_snapshot_is_not_active()
    {
        var s = new AppSettings { State = new LockInState { Active = true, Snapshot = null } };
        s.Normalize();
        Assert.False(s.State.Active);
    }

    [Fact]
    public void Lock_in_with_a_snapshot_stays_active()
    {
        var s = new AppSettings { State = new LockInState { Active = true, Snapshot = new LevelSnapshot() } };
        s.Normalize();
        Assert.True(s.State.Active);
    }

    [Fact]
    public void Every_hotkey_action_has_exactly_one_binding()
    {
        var s = new AppSettings { Hotkeys = [HotkeyDefaults.For(HotkeyAction.ToggleLockIn), HotkeyDefaults.For(HotkeyAction.ToggleLockIn)] };
        s.Normalize();

        Assert.Equal(Enum.GetValues<HotkeyAction>().Length, s.Hotkeys.Count);
        Assert.Equal(s.Hotkeys.Count, s.Hotkeys.Select(h => h.Action).Distinct().Count());
    }

    [Fact]
    public void Window_size_is_kept_sane()
    {
        var s = new AppSettings { Window = new WindowSettings { Width = double.NaN, Height = 99999 } };
        s.Normalize();

        Assert.Equal(460, s.Window.Width);
        Assert.Equal(3000, s.Window.Height);
    }
}
