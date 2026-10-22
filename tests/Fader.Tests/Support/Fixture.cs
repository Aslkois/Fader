using Fader.Core.Actions;
using Fader.Core.Apps;
using Fader.Core.Audio;
using Fader.Core.LockIn;
using Fader.Core.Profiles;
using Fader.Core.Settings;

namespace Fader.Tests.Support;

/// <summary>Hand-advanced clock so rate limits can be tested without sleeping.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(_ticks);
    public void Advance(TimeSpan by) => _ticks += (long)by.TotalMilliseconds;
}

/// <summary>A temp settings folder plus the full core service graph on a mock backend.</summary>
public sealed class Fixture : IDisposable
{
    public const string Discord = @"C:\Users\x\AppData\Local\Discord\app-1.0.9001\Discord.exe";
    public const string Spotify = @"C:\Users\x\AppData\Roaming\Spotify\Spotify.exe";
    public const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    public const string Game = @"D:\Games\Shooter\shooter.exe";
    public const string Notes = @"C:\Tools\notes.exe";

    public Fixture(bool ready = true)
    {
        Directory = Path.Combine(Path.GetTempPath(), "fader-tests-" + Guid.NewGuid().ToString("N"));
        Settings = new SettingsService(Directory, TimeSpan.FromHours(1));
        Settings.Load();
        Engine = new MockAudioEngine();
        Mixer = new MixerService(Engine, sync: null);
        Catalog = new AppCatalog(Settings);
        Profiles = new ProfileService(Settings);
        LockIn = new LockInController(Mixer, Profiles, Catalog, Settings, Time);
        Actions = new QuickActions(Mixer, Settings);
        if (ready) Engine.CompleteInitialScan();
    }

    public string Directory { get; }
    public ManualTimeProvider Time { get; } = new();
    public SettingsService Settings { get; }
    public MockAudioEngine Engine { get; }
    public MixerService Mixer { get; }
    public AppCatalog Catalog { get; }
    public ProfileService Profiles { get; }
    public LockInController LockIn { get; }
    public QuickActions Actions { get; }

    public AppGroup Group(string exePath) => Mixer.FindGroup(AppIdentity.FromPath(exePath))!;

    public float Volume(string exePath) => Group(exePath).Volume;

    /// <summary>Adds the usual evening: a game, Discord, Spotify and a browser, at distinct levels.</summary>
    public void Evening()
    {
        Engine.AddSession(MockAudioEngine.Session("game", Game, 1.0f, pid: 1));
        Engine.AddSession(MockAudioEngine.Session("discord", Discord, 0.9f, pid: 2));
        Engine.AddSession(MockAudioEngine.Session("spotify", Spotify, 0.8f, pid: 3));
        Engine.AddSession(MockAudioEngine.Session("chrome", Chrome, 0.7f, pid: 4, state: SessionState.Inactive));
    }

    /// <summary>A second Settings instance reading the same file, as after restarting FADER.</summary>
    public SettingsService Reload()
    {
        Settings.Flush();
        var reloaded = new SettingsService(Directory, TimeSpan.FromHours(1));
        reloaded.Load();
        return reloaded;
    }

    public void Dispose()
    {
        LockIn.Dispose();
        Mixer.Dispose();
        Settings.Dispose();
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch (IOException) { }
    }
}
