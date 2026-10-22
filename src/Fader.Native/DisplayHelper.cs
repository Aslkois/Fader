using System.Runtime.InteropServices;

namespace Fader.Native;

/// <summary>Window-manager helpers that have no WPF equivalent.</summary>
public static class DisplayHelper
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Work area (physical pixels) of the monitor that holds the foreground window.</summary>
    public static (int Left, int Top, int Right, int Bottom)? ForegroundWorkArea()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero) return null;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return null;
        return (info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    /// <summary>Pixel size Windows wants for a notification-area icon on this display.</summary>
    public static int SmallIconSize()
    {
        try
        {
            const int SM_CXSMICON = 49;
            var size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForSystem());
            return size > 0 ? size : 16;
        }
        catch (Exception)
        {
            return 16;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>
    /// Moves a top-level popup so it opens beside a screen point (physical pixels) and stays inside that monitor's work area.
    /// Done in pixels on purpose: WPF's own placement misreads mixed-DPI setups for popups opened from the tray.
    /// </summary>
    public static void PlaceNearPoint(IntPtr hwnd, int pointX, int pointY, double transparentMarginPx = 0)
    {
        var margin = (int)Math.Round(transparentMarginPx);
        const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_NOZORDER = 0x4;
        if (!GetWindowRect(hwnd, out var window)) return;
        var width = window.Right - window.Left;
        var height = window.Bottom - window.Top;

        var monitor = MonitorFromPoint(new POINT { X = pointX, Y = pointY }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return;
        var work = info.rcWork;

        // Open up and to the left of the point when it sits near the bottom or right edge (the tray), like Windows menus.
        // The window may include a transparent margin (for a shadow); line up the visible part, not the window.
        var x = pointX + width - 2 * margin > work.Right ? pointX - width + margin : pointX - margin;
        var y = pointY + height - 2 * margin > work.Bottom ? pointY - height + margin : pointY - margin;
        x = Math.Clamp(x, work.Left - margin, Math.Max(work.Left - margin, work.Right - width + margin));
        y = Math.Clamp(y, work.Top - margin, Math.Max(work.Top - margin, work.Bottom - height + margin));
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER);
    }

    /// <summary>Asks Windows 11 for rounded corners on a borderless window. No-op on Windows 10.</summary>
    public static void UseRoundedCorners(IntPtr hwnd)
    {
        try
        {
            var preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch (Exception)
        {
            // Older Windows builds do not know the attribute.
        }
    }
}
