using System.Text;

namespace Fader.Core.Hotkeys;

/// <summary>Values match the Win32 MOD_* flags used by RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

public enum HotkeyAction
{
    ToggleLockIn,
    ToggleChatMute,
    ChatVolumeUp,
    ChatVolumeDown,
    RestoreLevels,
    ToggleWindow,
}

public sealed class HotkeyBinding
{
    public HotkeyAction Action { get; set; }
    public HotkeyModifiers Modifiers { get; set; }

    /// <summary>Windows virtual-key code.</summary>
    public uint Key { get; set; }

    public bool Enabled { get; set; } = true;

    public bool IsAssigned => Key != 0 && Modifiers != HotkeyModifiers.None;

    public HotkeyBinding Clone() => new() { Action = Action, Modifiers = Modifiers, Key = Key, Enabled = Enabled };

    public override string ToString() => HotkeyFormatter.Format(Modifiers, Key);
}

public static class HotkeyDefaults
{
    public static List<HotkeyBinding> Create() =>
    [
        Bind(HotkeyAction.ToggleLockIn, 0x4C),      // L
        Bind(HotkeyAction.ToggleChatMute, 0x4D),    // M
        Bind(HotkeyAction.ChatVolumeUp, 0x21),      // Page Up
        Bind(HotkeyAction.ChatVolumeDown, 0x22),    // Page Down
        Bind(HotkeyAction.RestoreLevels, 0x52),     // R
        Bind(HotkeyAction.ToggleWindow, 0x46),      // F
    ];

    public static HotkeyBinding For(HotkeyAction action) => Create().First(b => b.Action == action);

    private static HotkeyBinding Bind(HotkeyAction action, uint vk) => new()
    {
        Action = action,
        Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
        Key = vk,
    };

    public static string Describe(HotkeyAction action, string chatName) => action switch
    {
        HotkeyAction.ToggleLockIn => "Toggle LOCK IN",
        HotkeyAction.ToggleChatMute => $"Toggle {chatName} mute",
        HotkeyAction.ChatVolumeUp => $"Raise {chatName} volume",
        HotkeyAction.ChatVolumeDown => $"Lower {chatName} volume",
        HotkeyAction.RestoreLevels => "Restore previous levels",
        HotkeyAction.ToggleWindow => "Show or hide FADER",
        _ => action.ToString(),
    };
}

public static class HotkeyFormatter
{
    public static string Format(HotkeyModifiers modifiers, uint vk)
    {
        if (vk == 0) return "Not set";
        var sb = new StringBuilder();
        if (modifiers.HasFlag(HotkeyModifiers.Control)) sb.Append("Ctrl+");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) sb.Append("Alt+");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) sb.Append("Shift+");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) sb.Append("Win+");
        sb.Append(KeyName(vk));
        return sb.ToString();
    }

    public static string KeyName(uint vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
        if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60);
        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "Print Screen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6A => "Num *",
            0x6B => "Num +",
            0x6D => "Num -",
            0x6E => "Num .",
            0x6F => "Num /",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            0xAD => "Volume Mute",
            0xAE => "Volume Down",
            0xAF => "Volume Up",
            0xB0 => "Next Track",
            0xB1 => "Previous Track",
            0xB3 => "Play/Pause",
            _ => $"Key 0x{vk:X2}",
        };
    }
}
