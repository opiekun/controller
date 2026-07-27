namespace KeyboardAnalogThrottle.Core.Abstractions;

/// <summary>
/// Runs the short diagnostic sweep for the virtual controller.
/// </summary>
public interface IControllerTestService
{
    event EventHandler<ControllerTestProgress>? ProgressChanged;

    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Describes one level sent during the virtual-controller diagnostic sweep.
/// </summary>
public sealed record ControllerTestProgress(int Step, int TotalSteps, bool IsLeftTrigger, byte Value);
