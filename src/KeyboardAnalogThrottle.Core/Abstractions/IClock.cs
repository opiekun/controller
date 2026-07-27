namespace KeyboardAnalogThrottle.Core.Abstractions;

/// <summary>
/// A monotonic clock and cancellable delay source for the emulation loop.
/// </summary>
public interface IClock
{
    TimeSpan GetTimestamp();

    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
