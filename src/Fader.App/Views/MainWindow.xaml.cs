using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Fader.App.Services;
using Fader.App.ViewModels;
using Fader.Native;

namespace Fader.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppServices _services;

    public MainWindow(MainViewModel viewModel, AppServices services)
    {
        _viewModel = viewModel;
        _services = services;
        InitializeComponent();
        DataContext = viewModel;
        ApplySavedBounds();

        SourceInitialized += (_, _) => DisplayHelper.UseRoundedCorners(new WindowInteropHelper(this).Handle);
        IsVisibleChanged += (_, _) => UpdateMeters();
        StateChanged += OnStateChanged;
        SizeChanged += (_, _) => RememberBounds();
        LocationChanged += (_, _) => RememberBounds();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Meters are sampled only while someone can see them.</summary>
    private void UpdateMeters() =>
        _services.Engine.SetMetersEnabled(IsVisible && WindowState != WindowState.Minimized);

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // A fixed-size utility panel: maximizing adds nothing and breaks the custom frame.
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        UpdateMeters();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel.IsSettingsOpen && Keyboard.FocusedElement is not Controls.HotkeyBox)
        {
            _viewModel.IsSettingsOpen = false;
            e.Handled = true;
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ---- remembered position and size ----

    private void ApplySavedBounds()
    {
        var w = _services.Config.Window;
        Width = w.Width;
        Height = w.Height;

        if (w.Left is { } left && w.Top is { } top && IsOnScreen(left, top, w.Width, w.Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var window = new Rect(left, top, Math.Max(width, 100), Math.Max(height, 100));
        window.Intersect(screen);
        return !window.IsEmpty && window.Width >= 80 && window.Height >= 60;
    }

    private void RememberBounds()
    {
        if (!IsLoaded || WindowState != WindowState.Normal) return;
        var w = _services.Config.Window;
        w.Left = Left;
        w.Top = Top;
        w.Width = ActualWidth;
        w.Height = ActualHeight;
        _services.Settings.MarkDirty();
    }
}
