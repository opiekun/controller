using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

/// <summary>
/// Atomically associates suppressed keys with one never-reused keyboard capture session.
/// </summary>
internal sealed class CaptureSuppressedKeys
{
    private readonly Action? _afterStateChange;
    private Session _currentCapture = new(0);

    public CaptureSuppressedKeys()
    {
    }

    internal CaptureSuppressedKeys(Action afterStateChange)
    {
        ArgumentNullException.ThrowIfNull(afterStateChange);
        _afterStateChange = afterStateChange;
    }

    public Session CurrentCapture => Volatile.Read(ref _currentCapture);

    public Session BeginCapture(long captureGeneration)
    {
        if (captureGeneration == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(captureGeneration));
        }

        var capture = new Session(captureGeneration);
        Interlocked.Exchange(ref _currentCapture, capture);
        return capture;
    }

    public void EndCapture() => Interlocked.Exchange(ref _currentCapture, new Session(0));

    public bool IsCurrent(Session capture) =>
        capture.Generation != 0 && ReferenceEquals(Volatile.Read(ref _currentCapture), capture);

    public bool TryMark(InputKey key, Session capture)
    {
        var keyMask = unchecked((long)KeyboardSuppressionState.KeyMask(key));
        if (keyMask == 0)
        {
            return false;
        }

        while (IsCurrent(capture))
        {
            var observed = Volatile.Read(ref capture.SuppressedKeyMask);
            if ((observed & keyMask) != 0)
            {
                return IsCurrent(capture);
            }

            if (Interlocked.CompareExchange(
                    ref capture.SuppressedKeyMask,
                    observed | keyMask,
                    observed) == observed)
            {
                _afterStateChange?.Invoke();
                return IsCurrent(capture);
            }
        }

        return false;
    }

    public bool TryTake(InputKey key, Session capture)
    {
        var keyMask = unchecked((long)KeyboardSuppressionState.KeyMask(key));
        if (keyMask == 0)
        {
            return false;
        }

        while (IsCurrent(capture))
        {
            var observed = Volatile.Read(ref capture.SuppressedKeyMask);
            if ((observed & keyMask) == 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref capture.SuppressedKeyMask,
                    observed & ~keyMask,
                    observed) == observed)
            {
                _afterStateChange?.Invoke();
                return IsCurrent(capture);
            }
        }

        return false;
    }

    internal sealed class Session(long generation)
    {
        public long Generation { get; } = generation;

        internal long SuppressedKeyMask;
    }
}
