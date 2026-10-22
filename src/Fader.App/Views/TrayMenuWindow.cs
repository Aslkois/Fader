using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Fader.Native;

namespace Fader.App.Views;

public abstract record TrayMenuEntry;
public sealed record TrayMenuItem(string Text, Action Click, bool Checked = false, bool Enabled = true) : TrayMenuEntry;
public sealed record TrayMenuLabel(string Text) : TrayMenuEntry;
public sealed record TrayMenuSeparator : TrayMenuEntry;

/// <summary>
/// The tray icon's menu: a small themed borderless window placed in physical pixels beside the click point.
/// It closes when it loses focus, on Escape, and after an item is chosen.
/// </summary>
public sealed class TrayMenuWindow : Window
{
    private readonly int _pointX;
    private readonly int _pointY;

    public TrayMenuWindow(IEnumerable<TrayMenuEntry> entries, int pointX, int pointY)
    {
        _pointX = pointX;
        _pointY = pointY;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0;
        FontSize = 13;
        SetResourceReference(FontFamilyProperty, "Font.UI");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        UseLayoutRounding = true;
        System.Windows.Automation.AutomationProperties.SetName(this, "FADER menu");

        var stack = new StackPanel { MinWidth = 210 };
        KeyboardNavigation.SetDirectionalNavigation(stack, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetTabNavigation(stack, KeyboardNavigationMode.Cycle);
        foreach (var entry in entries) stack.Children.Add(Build(entry));

        var panel = new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.4, Color = Colors.Black },
        };
        panel.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceRaised");
        panel.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderStrong");
        Content = panel;

        Deactivated += (_, _) => CloseOnce();
        Closing += (_, _) => _closing = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CloseOnce();
        };
        ContentRendered += (_, _) => { Place(); Opacity = 1; };
        DpiChanged += (_, _) => Place();
    }

    private bool _closing;

    // Closing a window deactivates it, which would ask it to close again.
    private void CloseOnce()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    public void Present()
    {
        Show();
        Activate();
        WindowStyles.BringToForeground(new WindowInteropHelper(this).Handle);
        // Start keyboard navigation on the first enabled row so Up/Down and Enter work straight away.
        MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void Place() => DisplayHelper.PlaceNearPoint(new WindowInteropHelper(this).Handle, _pointX, _pointY, transparentMarginPx: 10 * VisualTreeHelper.GetDpi(this).DpiScaleX);

    private UIElement Build(TrayMenuEntry entry)
    {
        switch (entry)
        {
            case TrayMenuSeparator:
                var line = new Border { Height = 1, Margin = new Thickness(8, 4, 8, 4) };
                line.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
                return line;

            case TrayMenuLabel label:
                var text = new TextBlock { Text = label.Text, Margin = new Thickness(30, 8, 8, 4), FontSize = 12, FontWeight = FontWeights.SemiBold };
                text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
                return text;

            case TrayMenuItem item:
                var button = new Button { Content = item.Text, IsEnabled = item.Enabled, Tag = item.Checked ? "checked" : null };
                button.SetResourceReference(StyleProperty, "MenuRow");
                button.Click += (_, _) =>
                {
                    CloseOnce();
                    item.Click();
                };
                return button;

            default:
                throw new InvalidOperationException("Unknown menu entry.");
        }
    }
}
