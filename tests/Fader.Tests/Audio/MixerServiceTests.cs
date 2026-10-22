using Fader.Core.Apps;
using Fader.Core.Audio;
using Fader.Tests.Support;
using static Fader.Tests.Support.MockAudioEngine;

namespace Fader.Tests.Audio;

public class MixerServiceTests
{
    [Fact]
    public void Sessions_of_one_app_share_a_group()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord, 0.4f, pid: 1));
        f.Engine.AddSession(Session("a2", Fixture.Discord, 0.8f, pid: 2));

        var group = Assert.Single(f.Mixer.Groups);
        Assert.Equal(2, group.Sessions.Count);
        Assert.Equal(0.8f, group.Volume);
    }

    [Fact]
    public void A_new_version_folder_joins_the_existing_group()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", @"C:\x\Discord\app-1.0.1\Discord.exe", pid: 1));
        f.Engine.AddSession(Session("a2", @"C:\x\Discord\app-1.0.2\Discord.exe", pid: 2));

        Assert.Single(f.Mixer.Groups);
    }

    [Fact]
    public void Group_is_muted_only_when_every_session_is_muted()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord, muted: true, pid: 1));
        f.Engine.AddSession(Session("a2", Fixture.Discord, muted: false, pid: 2));
        Assert.False(f.Group(Fixture.Discord).Muted);

        f.Engine.ExternalChange("a2", muted: true);
        Assert.True(f.Group(Fixture.Discord).Muted);
    }

    [Fact]
    public void Group_is_playing_when_any_session_is_active()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Chrome, state: SessionState.Inactive, pid: 1));
        Assert.False(f.Group(Fixture.Chrome).IsPlaying);

        f.Engine.ChangeState("a1", SessionState.Active);
        Assert.True(f.Group(Fixture.Chrome).IsPlaying);
    }

    [Fact]
    public void Events_follow_the_group_lifecycle()
    {
        using var f = new Fixture();
        var added = 0; var removed = 0; var changed = 0; var sessionAdded = 0;
        f.Mixer.GroupAdded += _ => added++;
        f.Mixer.GroupRemoved += _ => removed++;
        f.Mixer.GroupChanged += _ => changed++;
        f.Mixer.SessionAdded += (_, _) => sessionAdded++;

        f.Engine.AddSession(Session("a1", Fixture.Discord, pid: 1));
        Assert.Equal((1, 0, 0, 0), (added, removed, changed, sessionAdded));

        f.Engine.AddSession(Session("a2", Fixture.Discord, pid: 2));
        Assert.Equal((1, 0, 1, 1), (added, removed, changed, sessionAdded));

        f.Engine.RemoveSession("a1");
        Assert.Equal((1, 0, 2, 1), (added, removed, changed, sessionAdded));

        f.Engine.RemoveSession("a2");
        Assert.Equal((1, 1, 2, 1), (added, removed, changed, sessionAdded));
        Assert.Empty(f.Mixer.Groups);
    }

    [Fact]
    public void An_expired_session_is_removed()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord));

        f.Engine.ChangeState("a1", SessionState.Expired);

        Assert.Empty(f.Mixer.Groups);
    }

    [Fact]
    public void Removing_an_unknown_session_is_ignored()
    {
        using var f = new Fixture();
        f.Engine.RemoveSession("never-seen");
        Assert.Empty(f.Mixer.Groups);
    }

    [Fact]
    public void Identity_falls_back_from_path_to_process_name_to_pid()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("n", exePath: null, processName: "Foo", pid: 7));
        f.Engine.AddSession(Session("p", exePath: null, pid: 9));
        f.Engine.AddSession(Session("s", system: true, pid: 0));

        Assert.True(f.Mixer.TryGetGroup("name:foo.exe", out _));
        Assert.True(f.Mixer.TryGetGroup("pid:9", out _));
        Assert.True(f.Mixer.TryGetGroup(AppIdentity.SystemSoundsKey, out _));
    }

    [Fact]
    public void SetGroupVolume_writes_every_session_and_raises_one_change()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord, 0.9f, pid: 1));
        f.Engine.AddSession(Session("a2", Fixture.Discord, 0.5f, pid: 2));
        var changed = 0;
        f.Mixer.GroupChanged += _ => changed++;

        f.Mixer.SetGroupVolume(f.Group(Fixture.Discord), 0.25f);

        Assert.Equal(0.25f, f.Volume(Fixture.Discord));
        Assert.Equal(2, f.Engine.VolumeWrites.Count(w => w.Volume == 0.25f));
        Assert.Equal(1, changed);
    }

    [Theory]
    [InlineData(1.7f, 1f)]
    [InlineData(-0.3f, 0f)]
    public void SetGroupVolume_clamps(float requested, float expected)
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord, 0.5f));

        f.Mixer.SetGroupVolume(f.Group(Fixture.Discord), requested);

        Assert.Equal(expected, f.Volume(Fixture.Discord));
        Assert.Equal(expected, f.Engine.VolumeWrites.Last().Volume);
    }

    [Fact]
    public void Setting_the_same_volume_raises_no_change()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord, 0.5f));
        var changed = 0;
        f.Mixer.GroupChanged += _ => changed++;

        f.Mixer.SetGroupVolume(f.Group(Fixture.Discord), 0.5f);

        Assert.Equal(0, changed);
    }

    [Fact]
    public void SetGroupMute_applies_to_all_sessions()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord, pid: 1));
        f.Engine.AddSession(Session("a2", Fixture.Discord, pid: 2));

        f.Mixer.SetGroupMute(f.Group(Fixture.Discord), true);

        Assert.True(f.Group(Fixture.Discord).Muted);
        Assert.True(f.Engine["a1"].Muted && f.Engine["a2"].Muted);
    }

    [Fact]
    public void SetSessionVolume_touches_only_that_session()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord, 0.9f, pid: 1));
        f.Engine.AddSession(Session("a2", Fixture.Discord, 0.9f, pid: 2));

        f.Mixer.SetSessionVolume(f.Group(Fixture.Discord), "a2", 0.1f);

        Assert.Equal(0.9f, f.Engine["a1"].Volume);
        Assert.Equal(0.1f, f.Engine["a2"].Volume);
    }

    [Fact]
    public void FADERs_own_writes_are_not_reported_as_external_changes()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord, 0.9f));
        var external = 0;
        f.Mixer.ExternalChange += _ => external++;

        f.Mixer.SetGroupVolume(f.Group(Fixture.Discord), 0.3f);

        Assert.Equal(0, external);
    }

    [Fact]
    public void Outside_level_changes_are_reported_but_state_changes_are_not()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord, 0.9f, state: SessionState.Inactive));
        var external = 0;
        f.Mixer.ExternalChange += _ => external++;

        f.Engine.ChangeState("a1", SessionState.Active);
        Assert.Equal(0, external);

        f.Engine.ExternalChange("a1", volume: 0.2f);
        Assert.Equal(1, external);
        Assert.Equal(0.2f, f.Volume(Fixture.Discord));

        f.Engine.ExternalChange("a1", volume: 0.2f);
        Assert.Equal(1, external);
    }

    [Fact]
    public void FindGroup_falls_back_to_exe_name()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Spotify));

        Assert.NotNull(f.Mixer.FindGroup(AppIdentity.FromPath(@"D:\elsewhere\Spotify.exe")));
        Assert.Null(f.Mixer.FindGroup(AppIdentity.FromPath(Fixture.Chrome)));
    }

    [Fact]
    public void Group_peak_is_the_loudest_session_and_resets_between_frames()
    {
        using var f = new Fixture();
        f.Engine.AddSession(Session("a1", Fixture.Discord, pid: 1));
        f.Engine.AddSession(Session("a2", Fixture.Discord, pid: 2));
        f.Engine.AddSession(Session("s1", Fixture.Spotify, pid: 3));

        f.Engine.RaiseMeters(new MeterFrame(0.5f, ["a1", "a2"], [0.2f, 0.6f]));

        Assert.Equal(0.6f, f.Group(Fixture.Discord).Peak);
        Assert.Equal(0f, f.Group(Fixture.Spotify).Peak);
        Assert.Equal(0.5f, f.Mixer.MasterPeak);

        f.Engine.RaiseMeters(new MeterFrame(0f, [], []));
        Assert.Equal(0f, f.Group(Fixture.Discord).Peak);
    }

    [Fact]
    public void Master_volume_and_mute_update_the_endpoint()
    {
        using var f = new Fixture();
        f.Engine.RaiseEndpoint(new EndpointState(Device, "Speakers", 1f, false, false, true, true));
        var raised = 0;
        f.Mixer.EndpointChanged += () => raised++;

        f.Mixer.SetMasterVolume(0.4f);
        f.Mixer.SetMasterMute(true);

        Assert.Equal(0.4f, f.Engine.MasterVolume);
        Assert.True(f.Engine.MasterMuted);
        Assert.Equal(0.4f, f.Mixer.Endpoint!.Volume);
        Assert.True(f.Mixer.Endpoint.Muted);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Ready_flag_and_status_follow_the_engine()
    {
        using var f = new Fixture(ready: false);
        Assert.False(f.Mixer.IsReady);

        f.Engine.CompleteInitialScan();
        f.Engine.RaiseStatus(EngineStatus.Error, "device lost");

        Assert.True(f.Mixer.IsReady);
        Assert.Equal(EngineStatus.Error, f.Mixer.Status);
        Assert.Equal("device lost", f.Mixer.StatusMessage);
    }

    [Fact]
    public void Disposed_mixer_ignores_the_engine()
    {
        using var f = new Fixture();
        f.Mixer.Dispose();

        f.Engine.AddSession(Session("a1", Fixture.Discord));

        Assert.Empty(f.Mixer.Groups);
    }

    [Fact]
    public void Updates_are_marshalled_to_the_supplied_context_and_meter_frames_are_dropped_not_queued()
    {
        var sync = new QueueingContext();
        var engine = new MockAudioEngine();
        using var mixer = new MixerService(engine, sync);
        var meterEvents = 0;
        mixer.MetersUpdated += () => meterEvents++;

        engine.AddSession(Session("a1", Fixture.Discord));
        Assert.Empty(mixer.Groups);
        sync.RunAll();
        Assert.Single(mixer.Groups);

        engine.RaiseMeters(new MeterFrame(0.1f, ["a1"], [0.1f]));
        engine.RaiseMeters(new MeterFrame(0.2f, ["a1"], [0.2f]));
        engine.RaiseMeters(new MeterFrame(0.3f, ["a1"], [0.3f]));
        Assert.Equal(1, sync.Pending);

        sync.RunAll();
        Assert.Equal(1, meterEvents);
        Assert.Equal(0.1f, mixer.MasterPeak);

        engine.RaiseMeters(new MeterFrame(0.4f, ["a1"], [0.4f]));
        Assert.Equal(1, sync.Pending);
    }

    private sealed class QueueingContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();
        public int Pending => _queue.Count;
        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
        public void RunAll()
        {
            while (_queue.Count > 0)
            {
                var (cb, st) = _queue.Dequeue();
                cb(st);
            }
        }
    }
}
