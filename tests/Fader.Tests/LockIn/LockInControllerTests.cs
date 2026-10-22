using Fader.Core.Apps;
using Fader.Core.Audio;
using Fader.Core.LockIn;
using Fader.Core.Profiles;
using Fader.Tests.Support;
using static Fader.Tests.Support.MockAudioEngine;

namespace Fader.Tests.LockIn;

public class LockInControllerTests
{
    private const string TeamSpeak = @"C:\Program Files\TeamSpeak 3\ts3client_win64.exe";

    private static Fixture Evening()
    {
        var f = new Fixture();
        f.Evening();
        return f;
    }

    [Fact]
    public void Activate_applies_the_lock_in_profile()
    {
        using var f = Evening();

        var result = f.LockIn.Activate();

        Assert.True(result.Success);
        Assert.Contains("4 apps", result.Message);
        Assert.True(f.LockIn.IsActive);
        Assert.Equal(1.0f, f.Volume(Fixture.Game), 3);
        Assert.Equal(0.20f, f.Volume(Fixture.Discord), 3);
        Assert.Equal(0.30f, f.Volume(Fixture.Spotify), 3);
        Assert.Equal(0.15f, f.Volume(Fixture.Chrome), 3);
    }

    [Fact]
    public void Activate_waits_for_the_first_audio_scan()
    {
        using var f = new Fixture(ready: false);

        var result = f.LockIn.Activate();

        Assert.False(result.Success);
        Assert.False(f.LockIn.IsActive);
    }

    [Fact]
    public void Activate_twice_keeps_the_original_snapshot()
    {
        using var f = Evening();
        f.LockIn.Activate();

        var again = f.LockIn.Activate();

        Assert.True(again.Success);
        var discord = f.LockIn.Snapshot!.Entries.Single(e => e.App.ExeName == "discord.exe");
        Assert.Equal(0.9f, discord.Volume, 3);
    }

    [Fact]
    public void Activate_with_nothing_matching_says_so()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("n", Fixture.Notes, state: SessionState.Inactive));

        var result = f.LockIn.Activate();

        Assert.True(result.Success);
        Assert.Contains("No running app matches", result.Message);
    }

    [Fact]
    public void A_lone_unclassified_app_that_is_playing_is_taken_as_the_game()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("n", Fixture.Notes));

        var result = f.LockIn.Activate();

        Assert.Contains("1 app set", result.Message);
        Assert.Equal(f.Group(Fixture.Notes).Key, f.LockIn.CurrentGameKey);
    }

    [Fact]
    public void Toggle_off_restores_the_previous_levels()
    {
        using var f = Evening();
        f.LockIn.Activate();

        var result = f.LockIn.Toggle();

        Assert.True(result.Success);
        Assert.False(f.LockIn.IsActive);
        Assert.Null(f.LockIn.Snapshot);
        Assert.Null(f.LockIn.CurrentGameKey);
        Assert.Equal(0.9f, f.Volume(Fixture.Discord), 3);
        Assert.Equal(0.8f, f.Volume(Fixture.Spotify), 3);
        Assert.Equal(0.7f, f.Volume(Fixture.Chrome), 3);
    }

    [Fact]
    public void Keep_current_policy_leaves_levels_alone()
    {
        using var f = Evening();
        f.Settings.Current.LockIn.RestorePolicy = RestorePolicy.KeepCurrent;
        f.LockIn.Activate();

        f.LockIn.Toggle();

        Assert.False(f.LockIn.IsActive);
        Assert.Equal(0.2f, f.Volume(Fixture.Discord), 3);
    }

    [Fact]
    public void Ask_each_time_defers_to_the_caller_only_when_interactive()
    {
        using var f = Evening();
        f.Settings.Current.LockIn.RestorePolicy = RestorePolicy.AskEachTime;
        f.LockIn.Activate();

        var asked = f.LockIn.Toggle(interactive: true);
        Assert.True(asked.NeedsDecision);
        Assert.True(f.LockIn.IsActive);

        var hotkey = f.LockIn.Toggle(interactive: false);
        Assert.False(hotkey.NeedsDecision);
        Assert.False(f.LockIn.IsActive);
        Assert.Equal(0.9f, f.Volume(Fixture.Discord), 3);
    }

    [Fact]
    public void Profile_mute_is_applied_and_undone()
    {
        using var f = Evening();
        f.Profiles.SetCategoryEntry(DefaultProfiles.LockInId, AppCategory.Chat, 20, mute: true);

        f.LockIn.Activate();
        Assert.True(f.Group(Fixture.Discord).Muted);

        f.LockIn.Deactivate(DeactivateMode.RestorePrevious);
        Assert.False(f.Group(Fixture.Discord).Muted);
    }

    [Fact]
    public void Deactivate_when_already_off_is_a_harmless_success()
    {
        using var f = Evening();
        Assert.True(f.LockIn.Deactivate(DeactivateMode.RestorePrevious).Success);
    }

    [Fact]
    public void Lock_in_survives_a_restart()
    {
        using var f = Evening();
        f.LockIn.Activate();

        var reloaded = f.Reload();

        Assert.True(reloaded.Current.State.Active);
        Assert.Equal(f.LockIn.CurrentGameKey, reloaded.Current.State.GameKey);
        Assert.Equal(4, reloaded.Current.State.Snapshot!.Entries.Count);
        Assert.Contains(reloaded.Current.State.Snapshot.Entries, e => e.App.Matches(AppIdentity.FromPath(Fixture.Discord)));
    }

    [Fact]
    public void Changed_is_raised_for_state_changes()
    {
        using var f = Evening();
        var raised = 0;
        f.LockIn.Changed += () => raised++;

        f.LockIn.Activate();
        f.LockIn.Deactivate(DeactivateMode.KeepCurrent);

        Assert.Equal(2, raised);
    }

    // ---- which app counts as "the game" ----

    [Fact]
    public void The_only_playing_unclassified_app_becomes_the_game()
    {
        using var f = Evening();
        f.LockIn.Activate();
        Assert.Equal(f.Group(Fixture.Game).Key, f.LockIn.CurrentGameKey);
    }

    [Fact]
    public void Foreground_app_is_the_game_when_it_is_unclassified()
    {
        using var f = Evening();
        f.Engine.AddSession(Session("notes", Fixture.Notes));

        f.LockIn.Activate(AppIdentity.FromPath(Fixture.Notes));

        Assert.Equal(f.Group(Fixture.Notes).Key, f.LockIn.CurrentGameKey);
    }

    [Fact]
    public void A_chat_app_in_the_foreground_is_never_the_game()
    {
        using var f = Evening();

        f.LockIn.Activate(AppIdentity.FromPath(Fixture.Discord));

        Assert.Equal(f.Group(Fixture.Game).Key, f.LockIn.CurrentGameKey);
    }

    [Fact]
    public void Two_candidates_and_no_foreground_means_no_game()
    {
        using var f = Evening();
        f.Engine.AddSession(Session("notes", Fixture.Notes));

        f.LockIn.Activate();

        Assert.Null(f.LockIn.CurrentGameKey);
        Assert.Equal(AppCategory.Other, f.LockIn.EffectiveCategory(f.Group(Fixture.Game)));
    }

    [Fact]
    public void The_chosen_game_counts_as_the_Game_category()
    {
        using var f = Evening();
        f.LockIn.Activate();
        Assert.Equal(AppCategory.Game, f.LockIn.EffectiveCategory(f.Group(Fixture.Game)));
    }

    [Fact]
    public void A_game_that_starts_later_is_adopted_when_it_is_in_front()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("discord", Fixture.Discord, 0.9f, pid: 2));
        f.LockIn.Activate();
        Assert.Null(f.LockIn.CurrentGameKey);
        f.LockIn.ForegroundProbe = () => AppIdentity.FromPath(Fixture.Game);

        f.Engine.AddSession(Session("game", Fixture.Game, 0.5f, pid: 1));

        Assert.Equal(f.Group(Fixture.Game).Key, f.LockIn.CurrentGameKey);
        Assert.Equal(1.0f, f.Volume(Fixture.Game), 3);
    }

    [Fact]
    public void A_game_that_starts_later_but_is_not_in_front_is_left_alone()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("discord", Fixture.Discord, 0.9f, pid: 2));
        f.LockIn.Activate();
        f.LockIn.ForegroundProbe = () => AppIdentity.FromPath(Fixture.Notes);

        f.Engine.AddSession(Session("game", Fixture.Game, 0.5f, pid: 1));

        Assert.Null(f.LockIn.CurrentGameKey);
        Assert.Equal(0.5f, f.Volume(Fixture.Game), 3);
    }

    // ---- apps that appear while LOCK IN is on ----

    [Fact]
    public void A_new_chat_app_is_set_to_target_and_remembered_for_restore()
    {
        using var f = Evening();
        f.LockIn.Activate();

        f.Engine.AddSession(Session("ts", TeamSpeak, 1.0f, pid: 9));

        Assert.Equal(0.2f, f.Volume(TeamSpeak), 3);
        Assert.Contains(f.LockIn.Snapshot!.Entries, e => e.App.ExeName == "ts3client_win64.exe" && e.Volume == 1.0f);

        f.LockIn.Toggle();
        Assert.Equal(1.0f, f.Volume(TeamSpeak), 3);
    }

    [Fact]
    public void A_second_session_of_a_known_app_is_set_without_touching_the_first()
    {
        using var f = Evening();
        f.LockIn.Activate();
        f.Engine.VolumeWrites.Clear();

        f.Engine.AddSession(Session("discord2", Fixture.Discord, 1.0f, pid: 5));

        Assert.Equal((("discord2", 0.2f)), f.Engine.VolumeWrites.Single());
    }

    [Fact]
    public void An_unclassified_new_app_is_left_alone()
    {
        using var f = Evening();
        f.LockIn.Activate();

        f.Engine.AddSession(Session("notes", Fixture.Notes, 0.6f, pid: 8));

        Assert.Equal(0.6f, f.Volume(Fixture.Notes), 3);
    }

    [Fact]
    public void New_apps_are_ignored_while_lock_in_is_off()
    {
        using var f = new Fixture();

        f.Engine.AddSession(Session("discord", Fixture.Discord, 0.9f));

        Assert.Equal(0.9f, f.Volume(Fixture.Discord), 3);
    }

    // ---- enforcement ----

    [Fact]
    public void Outside_changes_stick_unless_enforcement_is_on()
    {
        using var f = Evening();
        f.LockIn.Activate();

        f.Engine.ExternalChange("discord", volume: 0.9f);

        Assert.Equal(0.9f, f.Volume(Fixture.Discord), 3);
    }

    [Fact]
    public void Enforcement_puts_the_target_back()
    {
        using var f = Evening();
        f.Settings.Current.LockIn.EnforceProfile = true;
        f.LockIn.Activate();

        f.Engine.ExternalChange("discord", volume: 0.9f);

        Assert.Equal(0.2f, f.Volume(Fixture.Discord), 3);
    }

    [Fact]
    public void Enforcement_backs_off_after_a_burst_and_recovers_later()
    {
        using var f = Evening();
        f.Settings.Current.LockIn.EnforceProfile = true;
        f.LockIn.Activate();

        for (var i = 0; i < 3; i++)
        {
            f.Engine.ExternalChange("discord", volume: 0.9f);
            Assert.Equal(0.2f, f.Volume(Fixture.Discord), 3);
        }

        f.Engine.ExternalChange("discord", volume: 0.8f);
        Assert.Equal(0.8f, f.Volume(Fixture.Discord), 3);

        f.Time.Advance(TimeSpan.FromSeconds(6));
        f.Engine.ExternalChange("discord", volume: 0.7f);
        Assert.Equal(0.2f, f.Volume(Fixture.Discord), 3);
    }

    [Fact]
    public void Enforcement_does_nothing_for_apps_the_profile_does_not_cover()
    {
        using var f = Evening();
        f.Settings.Current.LockIn.EnforceProfile = true;
        f.Engine.AddSession(Session("notes", Fixture.Notes, 0.6f, pid: 8));
        f.LockIn.Activate();

        f.Engine.ExternalChange("notes", volume: 0.1f);

        Assert.Equal(0.1f, f.Volume(Fixture.Notes), 3);
    }

    // ---- one-shot profiles ----

    [Fact]
    public void ApplyProfile_sets_levels_without_turning_lock_in_on()
    {
        using var f = Evening();

        var result = f.LockIn.ApplyProfile(DefaultProfiles.GamingId);

        Assert.True(result.Success);
        Assert.False(f.LockIn.IsActive);
        Assert.Equal(DefaultProfiles.GamingId, f.Settings.Current.ActiveProfileId);
        Assert.Equal(0.6f, f.Volume(Fixture.Discord), 3);
        Assert.Equal(0.3f, f.Volume(Fixture.Spotify), 3);
        Assert.Equal(SnapshotKind.Profile, f.LockIn.Snapshot!.Kind);
        Assert.Null(f.LockIn.CurrentGameKey);
    }

    [Fact]
    public void RestorePrevious_undoes_a_profile()
    {
        using var f = Evening();
        f.LockIn.ApplyProfile(DefaultProfiles.GamingId);

        var result = f.LockIn.RestorePrevious();

        Assert.True(result.Success);
        Assert.Equal(0.9f, f.Volume(Fixture.Discord), 3);
        Assert.False(f.LockIn.HasSnapshot);
    }

    [Fact]
    public void RestorePrevious_without_a_snapshot_fails()
    {
        using var f = Evening();
        Assert.False(f.LockIn.RestorePrevious().Success);
    }

    [Fact]
    public void RestorePrevious_also_ends_lock_in()
    {
        using var f = Evening();
        f.LockIn.Activate();

        f.LockIn.RestorePrevious();

        Assert.False(f.LockIn.IsActive);
        Assert.Equal(0.9f, f.Volume(Fixture.Discord), 3);
    }

    [Fact]
    public void ApplyProfile_while_locked_in_keeps_the_original_snapshot()
    {
        using var f = Evening();
        f.LockIn.Activate();

        f.LockIn.ApplyProfile(DefaultProfiles.DefaultId);

        Assert.True(f.LockIn.IsActive);
        Assert.Equal(SnapshotKind.LockIn, f.LockIn.Snapshot!.Kind);
        Assert.Equal(1.0f, f.Volume(Fixture.Discord), 3);
    }

    [Fact]
    public void ApplyProfile_with_an_unknown_id_fails_and_changes_nothing()
    {
        using var f = Evening();
        Assert.False(f.LockIn.ApplyProfile("nope").Success);
        Assert.Equal(0.9f, f.Volume(Fixture.Discord), 3);
    }

    [Fact]
    public void Restore_skips_apps_that_have_quit()
    {
        using var f = Evening();
        f.LockIn.Activate();
        f.Engine.RemoveSession("spotify");

        var result = f.LockIn.RestorePrevious();

        Assert.True(result.Success);
        Assert.Contains("3 apps", result.Message);
    }
}
