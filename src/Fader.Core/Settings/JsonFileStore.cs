using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fader.Core.Settings;

public enum LoadOutcome
{
    /// <summary>The file was read normally.</summary>
    Loaded,
    /// <summary>There was no file yet; defaults are in use.</summary>
    Missing,
    /// <summary>The file was unreadable; the backup was used.</summary>
    RecoveredFromBackup,
    /// <summary>The file and its backup were unreadable; defaults are in use and the bad file was kept aside.</summary>
    ResetToDefaults,
}

public sealed record LoadResult<T>(T Value, LoadOutcome Outcome, string? CorruptCopyPath = null);

/// <summary>JSON persistence with atomic replacement, a one-generation backup and corruption recovery.</summary>
public static class JsonFileStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static LoadResult<T> Load<T>(string path, Func<T> createDefault) where T : class
    {
        var backup = path + ".bak";

        if (!File.Exists(path))
        {
            // A crash between deleting and replacing can leave only the backup.
            if (TryRead<T>(backup, out var orphan)) return new(orphan, LoadOutcome.RecoveredFromBackup);
            return new(createDefault(), LoadOutcome.Missing);
        }

        if (TryRead<T>(path, out var value)) return new(value, LoadOutcome.Loaded);

        var corruptCopy = KeepAside(path);
        if (TryRead<T>(backup, out var fromBackup)) return new(fromBackup, LoadOutcome.RecoveredFromBackup, corruptCopy);
        return new(createDefault(), LoadOutcome.ResetToDefaults, corruptCopy);
    }

    public static void Save<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, value, Options);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path)) File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
        else File.Move(temp, path);
    }

    private static bool TryRead<T>(string path, out T value) where T : class
    {
        value = null!;
        try
        {
            if (!File.Exists(path)) return false;
            using var stream = File.OpenRead(path);
            var parsed = JsonSerializer.Deserialize<T>(stream, Options);
            if (parsed is null) return false;
            value = parsed;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string? KeepAside(string path)
    {
        try
        {
            var target = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, target, overwrite: true);
            return target;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
