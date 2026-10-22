namespace Fader.Native;

public static class IconExtractor
{
    /// <summary>
    /// Returns an HICON for the executable at roughly the requested size, or IntPtr.Zero.
    /// The caller owns the handle and must release it with <see cref="Destroy"/>.
    /// </summary>
    public static IntPtr GetHandle(string path, int size)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return IntPtr.Zero;

        try
        {
            var icons = new IntPtr[1];
            var ids = new uint[1];
            var count = NativeMethods.PrivateExtractIcons(path, 0, size, size, icons, ids, 1, 0);
            if (count > 0 && count != uint.MaxValue && icons[0] != IntPtr.Zero) return icons[0];
        }
        catch (Exception)
        {
            // Fall through to the shell lookup.
        }

        var info = new NativeMethods.SHFILEINFO();
        var result = NativeMethods.SHGetFileInfo(path, 0, ref info,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
        return result == IntPtr.Zero ? IntPtr.Zero : info.hIcon;
    }

    public static void Destroy(IntPtr icon)
    {
        if (icon != IntPtr.Zero) NativeMethods.DestroyIcon(icon);
    }
}
