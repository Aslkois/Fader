using Fader.Core.Actions;
using Fader.Core.Apps;
using Fader.Core.Audio;
using Fader.Core.Hotkeys;
using Fader.Core.LockIn;
using Fader.Core.Profiles;
using Fader.Core.Settings;
using Fader.Native;

namespace Fader.App.Services;

/// <summary>What the services need from whatever owns the main window.</summary>
public interface IWindowHost
{
    bool IsWindowVisible { get; }
    void ShowWindow();
    void ToggleWindow(bool fromShortcut = false);
    void ShowSettings();
    void Quit();
}

/// <summary>Composition root: the long-lived services and the operations that span several of them.</summary>
public sealed class AppServices : IDisposable
{
    private MessageWindow? _messageWindow;

    public AppServices(SettingsService settings, IAudioEngine engine, SynchronizationContext sync)
    {
        Settings = settings;
        Engine = engine;
        Mixer = new MixerService(engine, sync);
        Catalog = new AppCatalog(settings);
        Profiles = new ProfileService(settings);
        LockIn = new LockInController(Mixer, Profiles, Catalog, settings) { ForegroundProbe = ForegroundApp };
        Actions = new QuickActions(Mixer, settings);
        Theme = new ThemeService();
    }

    public SettingsService Settings { get; }
    public AppSettings Config => Settings.Current;
    public IAudioEngine Engine { get; }
    public MixerService Mixer { get; }
    public AppCatalog Catalog { get; }
    public ProfileService Profiles { get; }
    public LockInController LockIn { get; }
    public QuickActions Actions { get; }
    public ThemeService Theme { get; }
    public IconService Icons { get; } = new();
    public OsdService Osd { get; } = new();
    public HotkeyManager? Hotkeys { get; private set; }
    public TrayService? Tray { get; private set; }
    public IWindowHost? Window { get; private set; }

    public IReadOnlyList<HotkeyRegistration> HotkeyResults { get; private set; } = [];

    /// <summary>A short message for the main window's status line (actions started from the window or tray).</summary>
    public event Action<string, bool>? Feedback;

    public event Action? HotkeyStatusChanged;

    public void Start(IWindowHost window)
    {
        Window = window;
        _messageWindow = new MessageWindow();

        Hotkeys = new HotkeyManager(_messageWindow);
        Hotkeys.Pressed += OnHotkey;
        ApplyHotkeys();

        Tray = new TrayService(this, _messageWindow);
        Tray.Start();

        Engine.Start(new OutputPreference(Config.OutputMode, Config.OutputDeviceId), Config.ShowAppsOnOtherDevices);
    }

    // ---- settings that touch the outside world ----

    public void ApplyOutputPreference() =>
        Engine.SetOutputPreference(new OutputPreference(Config.OutputMode, Config.OutputDeviceId));

    public void ApplyHotkeys()
    {
        if (Hotkeys is null) return;
        HotkeyResults = Hotkeys.Apply(Config.Hotkeys);
        HotkeyStatusChanged?.Invoke();
    }

    /// <summary>Writes or removes the per-user Run entry. Returns false if Windows refused.</summary>
    public bool SetStartWithWindows(bool enabled)
    {
        var ok = StartupRegistration.Set(enabled);
        Settings.MarkDirty();
        return ok;
    }

    public string HotkeyText(HotkeyAction action)
    {
        var b = Config.Hotkeys.FirstOrDefault(h => h.Action == action);
        var reg = HotkeyResults.FirstOrDefault(r => r.Action == action);
        return b is { IsAssigned: true, Enabled: true } && reg is { Status: HotkeyStatus.Registered } ? b.ToString() : string.Empty;
    }

    // ---- actions shared by window, tray and hotkeys ----

    public AppIdentity? ForegroundApp()
    {
        var pid = ProcessInfo.ForegroundProcessId();
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null;
        var path = ProcessInfo.TryGetImagePath(pid);
        return path is null ? null : AppIdentity.FromPath(path);
    }

    public ActionOutcome ToggleLockIn(bool interactive)
    {
        var outcome = LockIn.Toggle(ForegroundApp(), interactive);
        UpdateTray();
        return outcome;
    }

    public ActionOutcome ApplyProfile(string profileId)
    {
        var outcome = LockIn.ApplyProfile(profileId, ForegroundApp());
        Report(outcome, fromHotkey: false);
        return outcome;
    }

    public void UpdateTray() => Tray?.Refresh();

    /// <summary>Shows the result of an action where the user is looking: on-screen for shortcuts, in the window otherwise.</summary>
    public void Report(ActionOutcome outcome, bool fromHotkey)
    {
        if (string.IsNullOrEmpty(outcome.Message)) return;
        if (fromHotkey)
        {
            if (Config.ShowHotkeyFeedback) Osd.Show(outcome.Message, outcome.Success);
        }
        else
        {
            Feedback?.Invoke(outcome.Message, outcome.Success);
        }
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleLockIn:
                Report(ToggleLockIn(interactive: false), fromHotkey: true);
                break;
            case HotkeyAction.ToggleChatMute:
                Report(Actions.ToggleChatMute(), fromHotkey: true);
                break;
            case HotkeyAction.ChatVolumeUp:
                Report(Actions.ChatVolume(+1), fromHotkey: true);
                break;
            case HotkeyAction.ChatVolumeDown:
                Report(Actions.ChatVolume(-1), fromHotkey: true);
                break;
            case HotkeyAction.RestoreLevels:
                Report(LockIn.RestorePrevious(), fromHotkey: true);
                UpdateTray();
                break;
            case HotkeyAction.ToggleWindow:
                Window?.ToggleWindow(fromShortcut: true);
                break;
        }
    }

    public void Dispose()
    {
        Tray?.Dispose();
        Hotkeys?.Dispose();
        _messageWindow?.Dispose();
        Osd.Close();
        LockIn.Dispose();
        Mixer.Dispose();
        Theme.Dispose();
        Engine.Dispose();
    }
}
