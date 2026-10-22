using System.Collections.Concurrent;
using Fader.Audio;
using Fader.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Fader.Tests.Integration;

/// <summary>
/// Exercises the real backend using this test process as the "application": it opens an audio session by
/// playing a quiet tone, then checks that FADER sees it, controls it, meters it and tells its own writes
/// apart from outside changes. Needs a working default output device.
/// </summary>
public class WasapiEngineIntegrationTests
{
    private static async Task<T?> WaitFor<T>(Func<T?> probe, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var value = probe();
            if (value is not null) return value;
            await Task.Delay(25);
        }
        return null;
    }

    /// <summary>Reads the volume Windows itself reports for this process's session, bypassing FADER.</summary>
    private static (float Volume, bool Muted)? ReadWindowsVolume(uint pid)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.GetProcessID == pid) return (s.SimpleAudioVolume.Volume, s.SimpleAudioVolume.Mute);
        }
        return null;
    }

    private static void SetWindowsVolumeExternally(uint pid, float volume)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;
        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.GetProcessID == pid) { s.SimpleAudioVolume.Volume = volume; return; }
        }
    }

    [RealAudioFact]
    public async Task Engine_discovers_controls_meters_and_classifies_changes()
    {
        var pid = (uint)Environment.ProcessId;
        var updates = new ConcurrentQueue<SessionUpdate>();
        var frames = new ConcurrentQueue<MeterFrame>();
        var endpoints = new ConcurrentQueue<EndpointState?>();
        using var ready = new ManualResetEventSlim();

        using var engine = new WasapiAudioEngine();
        engine.SessionUpdated += updates.Enqueue;
        engine.MetersUpdated += frames.Enqueue;
        engine.EndpointChanged += endpoints.Enqueue;
        engine.InitialScanCompleted += ready.Set;
        engine.Start(new OutputPreference(OutputMode.FollowMultimedia, null), includeOtherDevices: false);

        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "engine never finished its first scan");
        var endpoint = await WaitFor(() => endpoints.LastOrDefault(e => e is not null), TimeSpan.FromSeconds(3));
        Assert.NotNull(endpoint);
        Assert.False(string.IsNullOrWhiteSpace(endpoint!.DeviceName));

        // A new session appears after the engine started: this exercises the session-created notification.
        var tone = new SignalGenerator(44100, 2) { Frequency = 440, Gain = 0.02, Type = SignalGeneratorType.Sin };
        using var output = new WasapiOut(AudioClientShareMode.Shared, 100);
        output.Init(tone.ToWaveProvider());
        output.Play();

        var mine = await WaitFor(() => updates.Select(u => u.Info).LastOrDefault(i => i.ProcessId == pid), TimeSpan.FromSeconds(8));
        Assert.NotNull(mine);
        Assert.False(string.IsNullOrEmpty(mine!.ExePath), "executable path should be resolved");

        // Our own write: Windows must report the new level, and FADER must not call it an outside change.
        while (updates.TryDequeue(out _)) { }
        engine.SetSessionVolume(mine.Key, 0.25f);
        await Task.Delay(800);
        var afterWrite = ReadWindowsVolume(pid);
        Assert.NotNull(afterWrite);
        Assert.InRange(afterWrite!.Value.Volume, 0.24f, 0.26f);
        Assert.DoesNotContain(updates, u => u.Info.ProcessId == pid && u.ExternalChange);

        // Mute is independent of volume.
        engine.SetSessionMute(mine.Key, true);
        await Task.Delay(500);
        var muted = ReadWindowsVolume(pid);
        Assert.True(muted!.Value.Muted);
        Assert.InRange(muted.Value.Volume, 0.24f, 0.26f);
        engine.SetSessionMute(mine.Key, false);

        // A change made behind FADER's back (like the Windows volume mixer) must arrive flagged as external.
        while (updates.TryDequeue(out _)) { }
        SetWindowsVolumeExternally(pid, 0.6f);
        var external = await WaitFor(
            () => updates.FirstOrDefault(u => u.Info.ProcessId == pid && u.ExternalChange && Math.Abs(u.Info.Volume - 0.6f) < 0.02f),
            TimeSpan.FromSeconds(3));
        Assert.NotNull(external);

        // Meters report real signal for the playing session.
        engine.SetSessionVolume(mine.Key, 1.0f);
        engine.SetMetersEnabled(true);
        var metered = await WaitFor(() =>
        {
            foreach (var f in frames)
            {
                var i = Array.IndexOf(f.Keys, mine.Key);
                if (i >= 0 && f.Peaks[i] > 0.005f) return f;
            }
            return null;
        }, TimeSpan.FromSeconds(5));
        Assert.NotNull(metered);

        engine.SetMetersEnabled(false);
        await Task.Delay(300);
        while (frames.TryDequeue(out _)) { }
        await Task.Delay(400);
        Assert.True(frames.Count <= 2, "meters must stop when disabled");

        // Stopping playback and releasing the stream must not leave FADER holding stale state.
        output.Stop();
    }

    [RealAudioFact]
    public async Task Engine_reports_devices_and_master_volume()
    {
        var devices = new ConcurrentQueue<IReadOnlyList<AudioDevice>>();
        using var ready = new ManualResetEventSlim();
        using var engine = new WasapiAudioEngine();
        engine.DevicesChanged += devices.Enqueue;
        engine.InitialScanCompleted += ready.Set;
        EndpointState? endpoint = null;
        engine.EndpointChanged += e => endpoint = e;
        engine.Start(new OutputPreference(OutputMode.FollowMultimedia, null), includeOtherDevices: false);

        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
        var list = await WaitFor(() => devices.LastOrDefault(), TimeSpan.FromSeconds(3));
        Assert.NotNull(list);
        Assert.NotEmpty(list!);
        Assert.Contains(list, d => d.IsDefaultMultimedia);
        Assert.NotNull(endpoint);
        Assert.InRange(endpoint!.Volume, 0f, 1f);
    }
}
