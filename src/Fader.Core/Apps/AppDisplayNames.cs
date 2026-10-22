using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Fader.Core.Apps;

/// <summary>Resolves a human-readable app name from the executable's version resource, cached per path.</summary>
public static class AppDisplayNames
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Name that needs no disk access, used until <see cref="Resolve"/> completes.</summary>
    public static string Fallback(AppIdentity identity)
    {
        if (identity.IsSystemSounds) return "System sounds";
        var name = System.IO.Path.GetFileNameWithoutExtension(identity.ExeName);
        if (string.IsNullOrEmpty(name)) return identity.ExeName;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
    }

    public static string Resolve(AppIdentity identity)
    {
        if (identity.IsSystemSounds) return "System sounds";
        var path = identity.Path;
        if (string.IsNullOrEmpty(path)) return Fallback(identity);
        return Cache.GetOrAdd(path, static (p, id) => ReadFromFile(p) ?? Fallback(id), identity);
    }

    private static string? ReadFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            foreach (var candidate in new[] { info.FileDescription, info.ProductName })
            {
                var c = candidate?.Trim();
                if (string.IsNullOrEmpty(c)) continue;
                if (c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                return c;
            }
        }
        catch (Exception)
        {
            // Version info is cosmetic; the fallback name is good enough.
        }
        return null;
    }
}
