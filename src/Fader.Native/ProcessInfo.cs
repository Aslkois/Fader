using System.Diagnostics;
using System.Text;

namespace Fader.Native;

public static class ProcessInfo
{
    /// <summary>
    /// Full path of a process's executable. Uses the limited-information access right so it works
    /// for most processes without elevation; returns null when Windows refuses.
    /// </summary>
    public static string? TryGetImagePath(uint processId)
    {
        if (processId == 0) return null;
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, (int)size) : null;
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public static string? TryGetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Process ID that owns the current foreground window, or 0.</summary>
    public static uint ForegroundProcessId()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return 0;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static string? ForegroundImagePath() => TryGetImagePath(ForegroundProcessId());
}
