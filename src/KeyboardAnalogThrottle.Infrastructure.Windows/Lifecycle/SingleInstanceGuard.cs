namespace KeyboardAnalogThrottle.Infrastructure.Windows.Lifecycle;

/// <summary>
/// Holds the process-wide mutex while this application instance is running.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string MutexName = "KeyboardAnalogThrottle.SingleInstance";

    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        try
        {
            return mutex.WaitOne(TimeSpan.Zero)
                ? new SingleInstanceGuard(mutex, ownsMutex: true)
                : DisposeAndReturnNull(mutex);
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstanceGuard(mutex, ownsMutex: true);
        }
    }

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }

    private static SingleInstanceGuard? DisposeAndReturnNull(Mutex mutex)
    {
        mutex.Dispose();
        return null;
    }
}
