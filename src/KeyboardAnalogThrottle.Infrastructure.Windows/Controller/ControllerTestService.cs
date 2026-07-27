using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Emulation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Controller;

/// <summary>
/// Runs the Core diagnostic sequence against a short-lived ViGEm controller.
/// </summary>
public sealed class ControllerTestService : IControllerTestService
{
    private readonly VigemControllerFactory _controllerFactory;
    private readonly TimeSpan _stepDuration;
    private readonly ILogger<ControllerTestService> _logger;

    public ControllerTestService(
        VigemControllerFactory controllerFactory,
        TimeSpan? stepDuration = null,
        ILogger<ControllerTestService>? logger = null)
    {
        _controllerFactory = controllerFactory ?? throw new ArgumentNullException(nameof(controllerFactory));
        _stepDuration = stepDuration ?? TimeSpan.FromMilliseconds(500);
        _logger = logger ?? NullLogger<ControllerTestService>.Instance;
    }

    public event EventHandler<ControllerTestProgress>? ProgressChanged;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Virtual controller test started.");
        await using var controller = _controllerFactory.Create();
        var sequence = new ControllerTestSequence(_stepDuration);
        sequence.ProgressChanged += ForwardProgress;
        try
        {
            await sequence.RunAsync(controller, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Virtual controller test completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Virtual controller test cancelled.");
            throw;
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
