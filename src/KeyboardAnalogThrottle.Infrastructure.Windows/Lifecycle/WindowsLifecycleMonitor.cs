using Microsoft.Win32;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Lifecycle;

/// <summary>
/// Forwards Windows session-ending and suspend notifications to a non-blocking safety callback.
/// </summary>
public sealed class WindowsLifecycleMonitor : IDisposable
{
    private readonly Action _requestEmergencyStop;
    private int _started;
    private int _disposed;

    public WindowsLifecycleMonitor(Action requestEmergencyStop)
    {
        _requestEmergencyStop = requestEmergencyStop ?? throw new ArgumentNullException(nameof(requestEmergencyStop));
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || Interlocked.Exchange(ref _started, 0) == 0)
        {
            return;
        }

        SystemEvents.SessionEnding -= OnSessionEnding;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs eventArgs) => RequestEmergencyStop();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == PowerModes.Suspend)
        {
            RequestEmergencyStop();
        }
    }

    private void RequestEmergencyStop()
    {
        try
        {
            _requestEmergencyStop();
        }
        catch
        {
            // Windows notification threads must never be blocked or terminated by application cleanup.
        }
    }
}
