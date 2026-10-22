using Fader.Tests.Support;
using static Fader.Tests.Support.MockAudioEngine;

namespace Fader.Tests.Actions;

public class QuickActionsTests
{
    private static Fixture WithChat(float volume = 0.9f)
    {
        var f = new Fixture();
        f.Engine.AddSession(Session("discord", Fixture.Discord, volume, pid: 2));
        f.Engine.AddSession(Session("spotify", Fixture.Spotify, 0.8f, pid: 3));
        return f;
    }

    [Fact]
    public void Chat_groups_are_the_configured_executables_only()
    {
        using var f = WithChat();

        var group = Assert.Single(f.Actions.ChatGroups());

        Assert.Equal("discord.exe", group.Identity.ExeName);
    }

    [Fact]
    public void Custom_chat_targets_are_honored()
    {
        using var f = WithChat();
        f.Settings.Current.ChatTargets = ["spotify.exe"];

        Assert.Equal("spotify.exe", Assert.Single(f.Actions.ChatGroups()).Identity.ExeName);
    }

    [Fact]
    public void Toggle_mute_flips_between_muted_and_unmuted()
    {
        using var f = WithChat();

        var first = f.Actions.ToggleChatMute();
        Assert.True(first.Success);
        Assert.Equal("Discord muted", first.Message);
        Assert.True(f.Group(Fixture.Discord).Muted);
        Assert.True(f.Actions.IsChatMuted());

        var second = f.Actions.ToggleChatMute();
        Assert.Equal("Discord unmuted", second.Message);
        Assert.False(f.Group(Fixture.Discord).Muted);
    }

    [Fact]
    public void Toggle_mute_reports_when_chat_is_not_running()
    {
        using var f = new Fixture();

        var result = f.Actions.ToggleChatMute();

        Assert.False(result.Success);
        Assert.Equal("Discord is not playing audio", result.Message);
        Assert.False(f.Actions.IsChatMuted());
    }

    [Fact]
    public void Volume_moves_by_the_configured_step()
    {
        using var f = WithChat(0.5f);
        f.Settings.Current.VolumeStepPercent = 10;

        var up = f.Actions.ChatVolume(+1);
        Assert.Equal("Discord 60%", up.Message);
        Assert.Equal(0.6f, f.Volume(Fixture.Discord), 3);

        var down = f.Actions.ChatVolume(-1);
        Assert.Equal("Discord 50%", down.Message);
    }

    [Fact]
    public void Volume_stops_at_the_ends()
    {
        using var f = WithChat(0.98f);
        Assert.Equal("Discord 100%", f.Actions.ChatVolume(+1).Message);
        Assert.Equal("Discord 100%", f.Actions.ChatVolume(+1).Message);

        f.Mixer.SetGroupVolume(f.Group(Fixture.Discord), 0.02f);
        Assert.Equal("Discord 0%", f.Actions.ChatVolume(-1).Message);
    }

    [Fact]
    public void Raising_the_volume_unmutes_but_lowering_does_not()
    {
        using var f = WithChat(0.5f);
        f.Actions.ToggleChatMute();

        f.Actions.ChatVolume(-1);
        Assert.True(f.Group(Fixture.Discord).Muted);

        f.Actions.ChatVolume(+1);
        Assert.False(f.Group(Fixture.Discord).Muted);
    }

    [Fact]
    public void Volume_reports_when_chat_is_not_running()
    {
        using var f = new Fixture();
        Assert.False(f.Actions.ChatVolume(+1).Success);
    }
}
