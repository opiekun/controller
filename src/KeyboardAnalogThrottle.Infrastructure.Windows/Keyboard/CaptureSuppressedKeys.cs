using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

/// <summary>
/// Atomically associates suppressed keys with one keyboard capture generation.
/// </summary>
public sealed class CaptureSuppressedKeys
{
    private Session _session = new(0, 0);

    public void BeginCapture(long captureGeneration) =>
        Interlocked.Exchange(ref _session, new Session(captureGeneration, 0));

    public bool TryMark(InputKey key, long captureGeneration)
    {
        var keyMask = KeyboardSuppressionState.KeyMask(key);
        if (keyMask == 0)
        {
            return false;
        }

        while (true)
        {
            var observed = Volatile.Read(ref _session);
            if (observed.CaptureGeneration != captureGeneration)
            {
                return false;
            }

            if ((observed.SuppressedKeyMask & keyMask) != 0)
            {
                return true;
            }

            var updated = new Session(captureGeneration, observed.SuppressedKeyMask | keyMask);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _session, updated, observed), observed))
            {
                return true;
            }
        }
    }

    public bool TryTake(InputKey key, long captureGeneration)
    {
        var keyMask = KeyboardSuppressionState.KeyMask(key);
        if (keyMask == 0)
        {
            return false;
        }

        while (true)
        {
            var observed = Volatile.Read(ref _session);
            if (observed.CaptureGeneration != captureGeneration || (observed.SuppressedKeyMask & keyMask) == 0)
            {
                return false;
            }

            var updated = new Session(captureGeneration, observed.SuppressedKeyMask & ~keyMask);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _session, updated, observed), observed))
            {
                return true;
            }
        }
    }

    private sealed record Session(long CaptureGeneration, ulong SuppressedKeyMask);
}
