using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Abstractions;

/// <summary>
/// Supplies synchronized keyboard snapshots from a platform-specific hook.
/// </summary>
public interface IKeyboardInputSource : IAsyncDisposable
{
    InputHealth Health { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    InputSnapshot GetSnapshot();
}
