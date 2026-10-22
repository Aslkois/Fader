using System.Runtime.InteropServices;

namespace Fader.Native;

/// <summary>Notification-area icon using Shell_NotifyIcon directly (no WinForms dependency).</summary>
public sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = NativeMethods.WM_APP + 1;
    private const uint IconId = 1;

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private string _tooltip = "FADER";
    private bool _visible;
    private long _lastActivation;

    public TrayIcon(MessageWindow window)
    {
        _window = window;
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _window.AddHandler(OnMessage);
    }

    /// <summary>Primary click (mouse or keyboard).</summary>
    public event Action? Activated;

    /// <summary>Right click or the menu key. Receives the screen position in physical pixels.</summary>
    public event Action<int, int>? ContextRequested;

    public void Show(IntPtr icon, string tooltip)
    {
        _icon = icon;
        _tooltip = tooltip;
        var data = Build();
        if (!_visible)
        {
            _visible = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
            if (_visible)
            {
                data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
                NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
            }
        }
        else
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
        }
    }

    public void Update(IntPtr icon, string tooltip) => Show(icon, tooltip);

    private NativeMethods.NOTIFYICONDATA Build() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = IconId,
        uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private bool OnMessage(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = IntPtr.Zero;

        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer restarted: the icon is gone and must be added again.
            _visible = false;
            if (_icon != IntPtr.Zero) Show(_icon, _tooltip);
            return true;
        }

        if (message != CallbackMessage) return false;

        var notification = (uint)((long)lParam & 0xFFFF);
        switch (notification)
        {
            case NativeMethods.WM_LBUTTONUP:
            case NativeMethods.NIN_KEYSELECT:
                // Version 4 can report both a click and a select; act once.
                var now = Environment.TickCount64;
                if (now - _lastActivation > 250)
                {
                    _lastActivation = now;
                    Activated?.Invoke();
                }
                break;
            case NativeMethods.WM_CONTEXTMENU:
                NativeMethods.GetCursorPos(out var p);
                ContextRequested?.Invoke(p.X, p.Y);
                break;
        }
        return true;
    }

    public void Dispose()
    {
        if (!_visible) return;
        var data = Build();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        _visible = false;
    }
}
