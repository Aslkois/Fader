using System.ComponentModel;
using System.Runtime.InteropServices;
using Fader.Core.Hotkeys;

namespace Fader.Native;

public enum HotkeyStatus
{
    Registered,
    Disabled,
    Unassigned,
    /// <summary>Another application (or Windows) already owns this combination.</summary>
    InUseElsewhere,
    /// <summary>Two FADER actions were given the same combination; the first one wins.</summary>
    DuplicateInFader,
    Failed,
}

public sealed record HotkeyRegistration(HotkeyAction Action, HotkeyStatus Status, string? Message)
{
    public bool IsOk => Status is HotkeyStatus.Registered or HotkeyStatus.Disabled or HotkeyStatus.Unassigned;
}

/// <summary>System-wide shortcuts through RegisterHotKey. No keyboard hook is installed.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly MessageWindow _window;
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private int _nextId = 0x4600;

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        _window.AddHandler(OnMessage);
    }

    public event Action<HotkeyAction>? Pressed;

    /// <summary>Replaces every registration with the given bindings and reports what happened to each.</summary>
    public IReadOnlyList<HotkeyRegistration> Apply(IEnumerable<HotkeyBinding> bindings)
    {
        UnregisterAll();
        var results = new List<HotkeyRegistration>();
        var claimed = new HashSet<(HotkeyModifiers, uint)>();

        foreach (var b in bindings)
        {
            if (!b.IsAssigned) { results.Add(new(b.Action, HotkeyStatus.Unassigned, "No shortcut set")); continue; }
            if (!b.Enabled) { results.Add(new(b.Action, HotkeyStatus.Disabled, "Turned off")); continue; }
            if (!claimed.Add((b.Modifiers, b.Key)))
            {
                results.Add(new(b.Action, HotkeyStatus.DuplicateInFader, "Same shortcut as another FADER action"));
                continue;
            }

            var id = _nextId++;
            var mods = (uint)b.Modifiers;
            if (!AllowsRepeat(b.Action)) mods |= NativeMethods.MOD_NOREPEAT;

            if (NativeMethods.RegisterHotKey(_window.Handle, id, mods, b.Key))
            {
                _registered[id] = b.Action;
                results.Add(new(b.Action, HotkeyStatus.Registered, null));
            }
            else
            {
                var error = Marshal.GetLastWin32Error();
                results.Add(error == ErrorHotkeyAlreadyRegistered
                    ? new(b.Action, HotkeyStatus.InUseElsewhere, "Already used by another application")
                    : new(b.Action, HotkeyStatus.Failed, new Win32Exception(error).Message));
            }
        }
        return results;
    }

    // Holding the volume shortcuts should keep stepping; toggles should fire once per press.
    private static bool AllowsRepeat(HotkeyAction action) => action is HotkeyAction.ChatVolumeUp or HotkeyAction.ChatVolumeDown;

    private bool OnMessage(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (message != NativeMethods.WM_HOTKEY) return false;
        if (_registered.TryGetValue((int)wParam, out var action)) Pressed?.Invoke(action);
        return true;
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys) NativeMethods.UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
    }

    public void Dispose() => UnregisterAll();
}
