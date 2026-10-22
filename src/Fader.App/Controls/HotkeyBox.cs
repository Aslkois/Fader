using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fader.App.ViewModels;
using Fader.Core.Hotkeys;

namespace Fader.App.Controls;

/// <summary>
/// Click it, then press the shortcut you want. Requires Ctrl, Alt or Shift plus a key.
/// Backspace or Delete clears it; Tab and Escape leave the box.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
        nameof(Gesture), typeof(HotkeyGesture), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(default(HotkeyGesture), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnGestureChanged));

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        AllowDrop = false;
        Cursor = Cursors.Arrow;
        ContextMenu = null;
        UpdateText();
    }

    public HotkeyGesture Gesture
    {
        get => (HotkeyGesture)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    private static void OnGestureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HotkeyBox)d).UpdateText();

    private void UpdateText() => Text = Gesture.IsEmpty ? "Not set" : Gesture.ToString();

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = "Press a shortcut";
        SelectAll();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        UpdateText();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };

        if (key == Key.Tab) return;

        e.Handled = true;

        if (key == Key.Escape)
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            Text = Describe(Modifiers()) + "…";
            return;
        }

        var mods = Modifiers();
        if (mods == HotkeyModifiers.None)
        {
            if (key is Key.Back or Key.Delete)
            {
                Gesture = default;
                Text = "Not set";
            }
            else
            {
                Text = "Hold Ctrl, Alt or Shift first";
            }
            return;
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;
        Gesture = new HotkeyGesture(mods, vk);
        UpdateText();
    }

    private static HotkeyModifiers Modifiers()
    {
        var m = HotkeyModifiers.None;
        var k = Keyboard.Modifiers;
        if (k.HasFlag(ModifierKeys.Control)) m |= HotkeyModifiers.Control;
        if (k.HasFlag(ModifierKeys.Alt)) m |= HotkeyModifiers.Alt;
        if (k.HasFlag(ModifierKeys.Shift)) m |= HotkeyModifiers.Shift;
        return m;
    }

    private static string Describe(HotkeyModifiers m)
    {
        var parts = new List<string>();
        if (m.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (m.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        return parts.Count == 0 ? "Press a shortcut" : string.Join("+", parts) + "+";
    }
}
