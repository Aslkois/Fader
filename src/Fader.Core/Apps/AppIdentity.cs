using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Fader.Core.Apps;

/// <summary>
/// Stable identity for an application, independent of process IDs.
/// <see cref="Key"/> is the normalized executable path (or a name-based fallback when the path
/// cannot be read). Two identities match when their keys are equal or their executable names are equal,
/// which keeps profiles working across updates that move an app into a new versioned folder.
/// </summary>
public sealed record AppIdentity(string Key, string ExeName, string? Path)
{
    public const string SystemSoundsKey = "system-sounds";

    public static readonly AppIdentity SystemSounds = new(SystemSoundsKey, "system sounds", null);

    // Squirrel-style installers (Discord, Slack, GitHub Desktop) put each release in app-<version>.
    private static readonly Regex VersionedFolder = new(@"\\app-\d+(\.\d+)*\\", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [JsonIgnore]
    public bool IsSystemSounds => Key == SystemSoundsKey;

    public static AppIdentity FromPath(string path)
    {
        var trimmed = path.Trim();
        var exe = System.IO.Path.GetFileName(trimmed).ToLowerInvariant();
        return new AppIdentity(NormalizePath(trimmed), exe, trimmed);
    }

    public static AppIdentity FromProcessName(string processName)
    {
        var exe = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName.ToLowerInvariant()
            : processName.ToLowerInvariant() + ".exe";
        return new AppIdentity("name:" + exe, exe, null);
    }

    public static string NormalizePath(string path)
    {
        var p = path.Trim().Replace('/', '\\').ToLowerInvariant();
        return VersionedFolder.Replace(p, @"\app-*\");
    }

    public bool Matches(AppIdentity? other)
    {
        if (other is null) return false;
        if (IsSystemSounds || other.IsSystemSounds) return IsSystemSounds && other.IsSystemSounds;
        return string.Equals(Key, other.Key, StringComparison.Ordinal)
            || string.Equals(ExeName, other.ExeName, StringComparison.OrdinalIgnoreCase);
    }
}
