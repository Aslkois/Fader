using Fader.Core.Apps;
using Fader.Core.Hotkeys;
using Fader.Core.Profiles;
using Fader.Core.Settings;
using Fader.Tests.Support;

namespace Fader.Tests.Apps;

public class AppIdentityTests
{
    [Fact]
    public void FromPath_normalizes_case_and_separators()
    {
        var a = AppIdentity.FromPath(@"C:\Program Files\Google\Chrome\Application\chrome.exe");
        var b = AppIdentity.FromPath("c:/program files/google/chrome/application/CHROME.EXE");

        Assert.Equal(a.Key, b.Key);
        Assert.Equal("chrome.exe", a.ExeName);
    }

    [Fact]
    public void Versioned_install_folders_collapse_to_one_key()
    {
        var old = AppIdentity.FromPath(@"C:\Users\x\AppData\Local\Discord\app-1.0.9001\Discord.exe");
        var updated = AppIdentity.FromPath(@"C:\Users\x\AppData\Local\Discord\app-1.0.9002\Discord.exe");

        Assert.Equal(old.Key, updated.Key);
        Assert.Contains(@"\app-*\", old.Key);
    }

    [Fact]
    public void Matches_by_exe_name_when_folders_differ()
    {
        var installed = AppIdentity.FromPath(@"C:\Program Files\Spotify\Spotify.exe");
        var portable = AppIdentity.FromPath(@"D:\Apps\Spotify.exe");

        Assert.NotEqual(installed.Key, portable.Key);
        Assert.True(installed.Matches(portable));
    }

    [Fact]
    public void Different_executables_do_not_match()
    {
        Assert.False(AppIdentity.FromPath(Fixture.Chrome).Matches(AppIdentity.FromPath(Fixture.Spotify)));
        Assert.False(AppIdentity.FromPath(Fixture.Chrome).Matches(null));
    }

    [Fact]
    public void System_sounds_only_matches_itself()
    {
        Assert.True(AppIdentity.SystemSounds.Matches(AppIdentity.SystemSounds));
        Assert.False(AppIdentity.SystemSounds.Matches(AppIdentity.FromPath(Fixture.Chrome)));
        Assert.False(AppIdentity.FromPath(Fixture.Chrome).Matches(AppIdentity.SystemSounds));
        Assert.True(AppIdentity.SystemSounds.IsSystemSounds);
    }

    [Theory]
    [InlineData("Discord", "discord.exe")]
    [InlineData("Discord.exe", "discord.exe")]
    [InlineData("DISCORD.EXE", "discord.exe")]
    public void FromProcessName_always_ends_in_exe(string input, string exe)
    {
        var id = AppIdentity.FromProcessName(input);
        Assert.Equal(exe, id.ExeName);
        Assert.Equal("name:" + exe, id.Key);
        Assert.Null(id.Path);
    }

    [Fact]
    public void Path_less_identity_matches_its_installed_counterpart()
    {
        Assert.True(AppIdentity.FromProcessName("discord").Matches(AppIdentity.FromPath(Fixture.Discord)));
    }
}

public class KnownAppsTests
{
    [Theory]
    [InlineData(Fixture.Discord, AppCategory.Chat)]
    [InlineData(Fixture.Spotify, AppCategory.Music)]
    [InlineData(Fixture.Chrome, AppCategory.Browser)]
    [InlineData(Fixture.Game, AppCategory.Other)]
    public void Classify_uses_the_built_in_lists(string path, AppCategory expected)
    {
        Assert.Equal(expected, KnownApps.Classify(AppIdentity.FromPath(path)));
    }

    [Fact]
    public void System_sounds_are_always_system()
    {
        Assert.Equal(AppCategory.System, KnownApps.Classify(AppIdentity.SystemSounds));
    }

    [Fact]
    public void Classify_ignores_exe_case()
    {
        Assert.Equal(AppCategory.Chat, KnownApps.Classify(AppIdentity.FromProcessName("DISCORD")));
    }
}

public class AppDisplayNamesTests
{
    [Fact]
    public void Fallback_title_cases_the_exe_name()
    {
        Assert.Equal("Discord", AppDisplayNames.Fallback(AppIdentity.FromPath(Fixture.Discord)));
        Assert.Equal("System sounds", AppDisplayNames.Fallback(AppIdentity.SystemSounds));
    }

    [Fact]
    public void Resolve_uses_fallback_when_the_file_is_not_there()
    {
        Assert.Equal("Shooter", AppDisplayNames.Resolve(AppIdentity.FromPath(Fixture.Game)));
    }
}

public class AppCatalogTests
{
    [Fact]
    public void Uses_built_in_classification_by_default()
    {
        using var f = new Fixture();
        Assert.Equal(AppCategory.Chat, f.Catalog.Categorize(AppIdentity.FromPath(Fixture.Discord)));
        Assert.Equal(AppCategory.Other, f.Catalog.Categorize(AppIdentity.FromPath(Fixture.Game)));
    }

    [Fact]
    public void Override_wins_and_is_reported()
    {
        using var f = new Fixture();
        var game = AppIdentity.FromPath(Fixture.Game);

        f.Catalog.SetCategory(game, AppCategory.Game);

        Assert.Equal(AppCategory.Game, f.Catalog.Categorize(game));
        Assert.True(f.Catalog.IsOverridden(game));
    }

    [Fact]
    public void Setting_back_to_the_default_removes_the_override()
    {
        using var f = new Fixture();
        var chrome = AppIdentity.FromPath(Fixture.Chrome);

        f.Catalog.SetCategory(chrome, AppCategory.Music);
        f.Catalog.SetCategory(chrome, AppCategory.Browser);

        Assert.False(f.Catalog.IsOverridden(chrome));
        Assert.Empty(f.Settings.Current.CategoryOverrides);
    }

    [Fact]
    public void System_sounds_cannot_be_recategorized()
    {
        using var f = new Fixture();
        f.Catalog.SetCategory(AppIdentity.SystemSounds, AppCategory.Game);
        Assert.Equal(AppCategory.System, f.Catalog.Categorize(AppIdentity.SystemSounds));
    }

    [Fact]
    public void Name_based_override_applies_to_any_install_path()
    {
        using var f = new Fixture();
        f.Settings.Current.CategoryOverrides["name:shooter.exe"] = AppCategory.Game;

        Assert.Equal(AppCategory.Game, f.Catalog.Categorize(AppIdentity.FromPath(Fixture.Game)));
    }

    [Fact]
    public void Changing_a_category_raises_Changed_and_marks_settings_dirty()
    {
        using var f = new Fixture();
        var raised = 0;
        f.Catalog.Changed += () => raised++;

        f.Catalog.SetCategory(AppIdentity.FromPath(Fixture.Game), AppCategory.Game);
        f.Settings.Flush();

        Assert.Equal(1, raised);
        Assert.True(File.Exists(f.Settings.FilePath));
    }
}

public class HotkeyTests
{
    [Fact]
    public void Every_action_has_an_assigned_default_binding()
    {
        var defaults = HotkeyDefaults.Create();

        foreach (var action in Enum.GetValues<HotkeyAction>())
            Assert.True(defaults.Single(b => b.Action == action).IsAssigned, action.ToString());
    }

    [Fact]
    public void Default_bindings_do_not_collide()
    {
        var combos = HotkeyDefaults.Create().Select(b => (b.Modifiers, b.Key)).ToList();
        Assert.Equal(combos.Count, combos.Distinct().Count());
    }

    [Theory]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4Cu, "Ctrl+Alt+L")]
    [InlineData(HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x70u, "Shift+Win+F1")]
    [InlineData(HotkeyModifiers.Control, 0x21u, "Ctrl+Page Up")]
    [InlineData(HotkeyModifiers.Alt, 0x62u, "Alt+Num 2")]
    [InlineData(HotkeyModifiers.Alt, 0xFFu, "Alt+Key 0xFF")]
    [InlineData(HotkeyModifiers.Alt, 0u, "Not set")]
    public void Format_is_readable(HotkeyModifiers mods, uint vk, string expected)
    {
        Assert.Equal(expected, HotkeyFormatter.Format(mods, vk));
    }

    [Fact]
    public void A_binding_without_modifiers_is_not_assigned()
    {
        Assert.False(new HotkeyBinding { Action = HotkeyAction.ToggleLockIn, Key = 0x4C }.IsAssigned);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var original = HotkeyDefaults.For(HotkeyAction.ToggleLockIn);
        var copy = original.Clone();
        copy.Key = 0x41;
        Assert.NotEqual(original.Key, copy.Key);
    }
}
