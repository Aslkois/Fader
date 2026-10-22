using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Fader.Native;

namespace Fader.App.Views;

/// <summary>
/// A small non-activating message near the bottom of the screen, so a shortcut pressed inside a game
/// gives visible confirmation. Click-through and invisible to Alt+Tab. It cannot draw over exclusive-fullscreen games.
/// </summary>
public partial class OsdWindow : Window
{
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(1500);
    private readonly DispatcherTimer _timer = new() { Interval = Hold };

    public OsdWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => { if (Opacity < 0.05) Hide(); };
            BeginAnimation(OpacityProperty, fade);
        };
        SourceInitialized += (_, _) => WindowStyles.MakeOverlay(new WindowInteropHelper(this).Handle);
    }

    public void Present(string message, bool success)
    {
        Message.Text = message;
        Glyph.Text = success ? "" : "";
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        if (!IsVisible) Show();
        UpdateLayout();
        Place();

        _timer.Stop();
        _timer.Start();
    }

    private void Place()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var area = DisplayHelper.ForegroundWorkArea();
        double left, top, right, bottom;
        if (area is { } a)
        {
            left = a.Left / dpi.DpiScaleX; top = a.Top / dpi.DpiScaleY;
            right = a.Right / dpi.DpiScaleX; bottom = a.Bottom / dpi.DpiScaleY;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            left = wa.Left; top = wa.Top; right = wa.Right; bottom = wa.Bottom;
        }
        Left = left + (right - left - ActualWidth) / 2;
        Top = bottom - ActualHeight - 72;
    }
}
