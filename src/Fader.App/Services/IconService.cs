using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fader.Native;

namespace Fader.App.Services;

/// <summary>Loads application icons off the UI thread, once per executable, and hands back frozen images.</summary>
public sealed class IconService
{
    private readonly ConcurrentDictionary<string, Task<ImageSource?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<ImageSource?> GetAsync(string? exePath, int pixelSize)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return Task.FromResult<ImageSource?>(null);
        return _cache.GetOrAdd(exePath + "|" + pixelSize, _ => Task.Run(() => Load(exePath, pixelSize)));
    }

    private static ImageSource? Load(string path, int size)
    {
        var handle = IconExtractor.GetHandle(path, size);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            IconExtractor.Destroy(handle);
        }
    }
}
