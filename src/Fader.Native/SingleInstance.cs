namespace Fader.Native;

/// <summary>
/// Lets only one FADER run per user session. A second launch asks the first to show its window and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Fader.SingleInstance.6B1D";
    private const string SignalName = @"Local\Fader.ShowWindow.6B1D";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly RegisteredWaitHandle _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle signal)
    {
        _mutex = mutex;
        _signal = signal;
        _wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Raised on a thread-pool thread when another launch asked this instance to show itself.</summary>
    public event Action? ShowRequested;

    /// <summary>Returns the instance guard, or null after notifying the already-running copy.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            try
            {
                using var existing = EventWaitHandle.OpenExisting(SignalName);
                existing.Set();
            }
            catch (Exception)
            {
                // The first instance is still starting; nothing more to do.
            }
            return null;
        }

        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        return new SingleInstance(mutex, signal);
    }

    public void Dispose()
    {
        _wait.Unregister(null);
        _signal.Dispose();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
    }
}
