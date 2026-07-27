using KeyboardAnalogThrottle.Core.Emulation;

namespace KeyboardAnalogThrottle.Core.Abstractions;

/// <summary>
/// Manages the keyboard-to-controller emulation lifecycle.
/// </summary>
public interface IEmulationEngine : IAsyncDisposable
{
    EmulationState State { get; }

    event EventHandler<EmulationState>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task EmergencyResetAsync(CancellationToken cancellationToken);
}
