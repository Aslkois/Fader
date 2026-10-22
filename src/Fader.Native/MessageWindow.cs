using System.Runtime.InteropServices;

namespace Fader.Native;

public delegate bool MessageHandler(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);

/// <summary>
/// A hidden message-only window. The hotkey manager and tray icon share one so that neither depends on
/// a visible WPF window existing (FADER can run with its main window closed).
/// </summary>
public sealed class MessageWindow : IDisposable
{
    public const string ClassName = "FaderMessageWindow";

    private static readonly NativeMethods.WndProc StaticProc = DispatchMessage;
    private static readonly Dictionary<IntPtr, MessageWindow> Windows = new();
    private static bool _classRegistered;

    private readonly List<MessageHandler> _handlers = [];
    private bool _disposed;

    public MessageWindow()
    {
        var instance = NativeMethods.GetModuleHandle(null);
        if (!_classRegistered)
        {
            var wc = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(StaticProc),
                hInstance = instance,
                lpszClassName = ClassName,
            };
            if (NativeMethods.RegisterClassEx(ref wc) == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 1410) // ERROR_CLASS_ALREADY_EXISTS
                    throw new InvalidOperationException($"RegisterClassEx failed ({error}).");
            }
            _classRegistered = true;
        }

        Handle = NativeMethods.CreateWindowEx(0, ClassName, ClassName, 0, 0, 0, 0, 0,
            NativeMethods.HWND_MESSAGE, IntPtr.Zero, instance, IntPtr.Zero);
        if (Handle == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()}).");
        lock (Windows) Windows[Handle] = this;
    }

    public IntPtr Handle { get; }

    public void AddHandler(MessageHandler handler) => _handlers.Add(handler);

    private static IntPtr DispatchMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        MessageWindow? window;
        lock (Windows) Windows.TryGetValue(hWnd, out window);
        if (window is not null)
        {
            foreach (var handler in window._handlers)
            {
                if (handler(msg, wParam, lParam, out var result)) return result;
            }
        }
        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (Windows) Windows.Remove(Handle);
        NativeMethods.DestroyWindow(Handle);
    }
}
