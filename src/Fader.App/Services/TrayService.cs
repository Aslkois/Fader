using System.Drawing;
using System.Windows;
using Fader.App.Views;
using Fader.Core.Hotkeys;
using Fader.Native;

namespace Fader.App.Services;

/// <summary>Notification-area icon (Shell_NotifyIcon) and the themed menu it opens.</summary>
public sealed class TrayService : IDisposable
{
    private readonly AppServices _services;
    private readonly TrayIcon _tray;
    private readonly Icon _normal;
    private readonly Icon _locked;
    private TrayMenuWindow? _menu;

    public TrayService(AppServices services, MessageWindow window)
    {
        _services = services;
        _tray = new TrayIcon(window);
        var size = DisplayHelper.SmallIconSize();
        _normal = LoadIcon("pack://application:,,,/Assets/fader.ico", size);
        _locked = LoadIcon("pack://application:,,,/Assets/fader-locked.ico", size);
        _tray.Activated += () => _services.Window?.ToggleWindow();
        _tray.ContextRequested += ShowMenu;
    }

    public void Start()
    {
        Refresh();
        _services.LockIn.Changed += Refresh;
    }

    public void Refresh()
    {
        var locked = _services.LockIn.IsActive;
        _tray.Update((locked ? _locked : _normal).Handle, locked ? "FADER: LOCK IN is on" : "FADER");
    }

    private static Icon LoadIcon(string uri, int size)
    {
        var info = Application.GetResourceStream(new Uri(uri))!;
        using var stream = info.Stream;
        return new Icon(stream, new System.Drawing.Size(size, size));
    }

    private void ShowMenu(int cursorX, int cursorY)
    {
        _menu?.Close();
        _menu = new TrayMenuWindow(BuildEntries(), cursorX, cursorY);
        _menu.Closed += (_, _) => _menu = null;
        _menu.Present();
    }

    private List<TrayMenuEntry> BuildEntries()
    {
        var s = _services;
        var entries = new List<TrayMenuEntry>
        {
            new TrayMenuItem(s.Window?.IsWindowVisible == true ? "Hide FADER" : "Show FADER", () => s.Window?.ToggleWindow()),
            new TrayMenuSeparator(),
            new TrayMenuItem("LOCK IN", () => ToggleLockIn(), Checked: s.LockIn.IsActive),
            new TrayMenuItem($"Mute {s.Actions.ChatName()}", () => s.Report(s.Actions.ToggleChatMute(), fromHotkey: true),
                Checked: s.Actions.IsChatMuted(), Enabled: s.Actions.ChatGroups().Count > 0),
            new TrayMenuItem("Restore previous levels", () =>
            {
                s.Report(s.LockIn.RestorePrevious(), fromHotkey: true);
                s.UpdateTray();
            }, Enabled: s.LockIn.HasSnapshot),
            new TrayMenuSeparator(),
            new TrayMenuLabel("Apply profile"),
        };

        foreach (var profile in s.Profiles.Profiles)
        {
            var id = profile.Id;
            entries.Add(new TrayMenuItem(profile.Name, () => s.Report(s.ApplyProfile(id), fromHotkey: true),
                Checked: id == s.Config.ActiveProfileId));
        }

        entries.Add(new TrayMenuSeparator());
        entries.Add(new TrayMenuItem("Settings", () => s.Window?.ShowSettings()));
        entries.Add(new TrayMenuItem("Quit FADER", () => s.Window?.Quit()));
        return entries;
    }

    private void ToggleLockIn()
    {
        var outcome = _services.ToggleLockIn(interactive: false);
        _services.Report(outcome, fromHotkey: true);
    }

    public void Dispose()
    {
        _services.LockIn.Changed -= Refresh;
        _menu?.Close();
        _tray.Dispose();
        _normal.Dispose();
        _locked.Dispose();
    }
}
