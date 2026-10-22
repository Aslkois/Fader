namespace Fader.Core.Settings;

/// <summary>
/// Owns the settings file. Callers mutate <see cref="Current"/> and call <see cref="MarkDirty"/>;
/// writes are debounced so a slider drag or window resize costs one disk write, not hundreds.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly object _gate = new();
    private readonly Timer _debounce;
    private readonly TimeSpan _delay;
    private bool _dirty;
    private bool _disposed;

    public SettingsService(string directory, TimeSpan? debounce = null)
    {
        Directory = directory;
        FilePath = Path.Combine(directory, "settings.json");
        _delay = debounce ?? TimeSpan.FromMilliseconds(600);
        _debounce = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        Current = new AppSettings();
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fader");

    public string Directory { get; }
    public string FilePath { get; }
    public AppSettings Current { get; private set; }

    /// <summary>User-facing note when the file had to be repaired or replaced at startup.</summary>
    public string? LoadNotice { get; private set; }

    public string? LastSaveError { get; private set; }

    public void Load()
    {
        var result = JsonFileStore.Load(FilePath, () => new AppSettings());
        Current = result.Value;
        Current.Normalize();
        LoadNotice = result.Outcome switch
        {
            LoadOutcome.RecoveredFromBackup => "Your settings file was unreadable, so FADER restored the last good copy.",
            LoadOutcome.ResetToDefaults when result.CorruptCopyPath is not null =>
                $"Your settings file was unreadable. FADER started with defaults and kept the old file as {Path.GetFileName(result.CorruptCopyPath)}.",
            LoadOutcome.ResetToDefaults => "Your settings file was unreadable. FADER started with defaults.",
            _ => null,
        };
        if (result.Outcome != LoadOutcome.Loaded && result.Outcome != LoadOutcome.Missing) MarkDirty();
    }

    public void MarkDirty()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _dirty = true;
            _debounce.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes immediately if anything changed since the last write.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty) return;
            _dirty = false;
            try
            {
                JsonFileStore.Save(FilePath, Current);
                LastSaveError = null;
            }
            catch (Exception ex)
            {
                // Keep the data dirty so the next change (or exit) tries again.
                _dirty = true;
                LastSaveError = ex.Message;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Flush();
            _disposed = true;
        }
        _debounce.Dispose();
    }
}
