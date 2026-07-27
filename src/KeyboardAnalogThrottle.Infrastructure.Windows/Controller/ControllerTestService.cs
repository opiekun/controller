using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Emulation;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Controller;

/// <summary>
/// Runs the Core diagnostic sequence against a short-lived ViGEm controller.
/// </summary>
public sealed class ControllerTestService : IControllerTestService
{
    private readonly VigemControllerFactory _controllerFactory;
    private readonly TimeSpan _stepDuration;

    public ControllerTestService(VigemControllerFactory controllerFactory, TimeSpan? stepDuration = null)
    {
        _controllerFactory = controllerFactory ?? throw new ArgumentNullException(nameof(controllerFactory));
        _stepDuration = stepDuration ?? TimeSpan.FromMilliseconds(500);
    }

    public event EventHandler<ControllerTestProgress>? ProgressChanged;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var controller = _controllerFactory.Create();
        var sequence = new ControllerTestSequence(_stepDuration);
        sequence.ProgressChanged += ForwardProgress;
        try
        {
            await sequence.RunAsync(controller, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sequence.ProgressChanged -= ForwardProgress;
        }
    }

    private void ForwardProgress(object? sender, ControllerTestProgress progress)
    {
        var handlers = ProgressChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<ControllerTestProgress> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, progress);
            }
            catch
            {
                // Progress listeners are informational and must not interrupt the diagnostic sequence.
            }
        }
    }
}
