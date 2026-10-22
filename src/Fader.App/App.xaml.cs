using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Fader.App.Services;
using Fader.App.ViewModels;
using Fader.App.Views;
using Fader.Audio;
using Fader.Core.Settings;
using Fader.Native;

namespace Fader.App;

public partial class App : Application, IWindowHost
{
    private SingleInstance? _single;
    private AppServices? _services;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private bool _quitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _single = SingleInstance.TryAcquire();
        if (_single is null)
        {
            Shutdown();
            return;
        }
        _single.ShowRequested += () => Dispatcher.BeginInvoke(ShowWindow);

        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogCrash(args.ExceptionObject as Exception);
        SessionEnding += (_, _) => _services?.Settings.Flush();

        var settings = new SettingsService(SettingsService.DefaultDirectory);
        settings.Load();

        var sync = new DispatcherSynchronizationContext(Dispatcher);
        _services = new AppServices(settings, new WasapiAudioEngine(), sync);
        _services.Theme.Apply(settings.Current.Theme);
        _services.Start(this);

        var startHidden = e.Args.Contains(StartupRegistration.MinimizedArgument, StringComparer.OrdinalIgnoreCase);
        if (!startHidden) ShowWindow();
    }

    // ---- IWindowHost ----

    public bool IsWindowVisible => _window is { IsVisible: true, WindowState: not WindowState.Minimized };

    public void ShowWindow()
    {
        if (_services is null) return;
        if (_window is null)
        {
            _viewModel = new MainViewModel(_services);
            _window = new MainWindow(_viewModel, _services);
            _window.Closing += OnWindowClosing;
        }

        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        WindowStyles.BringToForeground(new WindowInteropHelper(_window).Handle);
        _services.Tray?.Refresh();
    }

    /// <summary>
    /// Hides the window if it is showing, otherwise shows it. A keyboard shortcut is pressed while another app has focus,
    /// so a window that is visible but buried is brought forward instead of hidden.
    /// </summary>
    public void ToggleWindow(bool fromShortcut = false)
    {
        if (IsWindowVisible && (!fromShortcut || _window!.IsActive)) _window!.Hide();
        else ShowWindow();
    }

    public void ShowSettings()
    {
        ShowWindow();
        if (_viewModel is not null) _viewModel.IsSettingsOpen = true;
    }

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _window?.Close();
        Shutdown();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_quitting) return;
        if (_services?.Config.CloseToTray == true)
        {
            e.Cancel = true;
            _window?.Hide();
            return;
        }
        // Close-to-tray is off: closing the window means exit.
        Quit();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _services?.Settings.Flush();
        _services?.Dispose();
        _services?.Settings.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }

    // ---- diagnostics ----

    private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.Handled = true;
        _viewModel?.ShowBanner("Something went wrong and FADER kept running. Details were saved to crash.log next to your settings.", isError: true);
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(SettingsService.DefaultDirectory);
            File.AppendAllText(
                Path.Combine(SettingsService.DefaultDirectory, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Nothing more can be done if the log itself cannot be written.
        }
    }
}
