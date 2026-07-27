using KeyboardAnalogThrottle.Core.Abstractions;

namespace KeyboardAnalogThrottle.Core.Emulation;

/// <summary>
/// Sends a bounded, visible trigger sweep while ensuring a cancellation or write failure leaves both triggers released.
/// </summary>
public sealed class ControllerTestSequence
{
    private static readonly byte[] Levels = [0, 64, 128, 191, 255, 0];
    private readonly TimeSpan _stepDuration;

    public ControllerTestSequence(TimeSpan? stepDuration = null)
    {
        _stepDuration = stepDuration ?? TimeSpan.FromMilliseconds(500);
        if (_stepDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(stepDuration), "The controller-test step duration cannot be negative.");
        }
    }

    public event EventHandler<ControllerTestProgress>? ProgressChanged;

    public async Task RunAsync(IVirtualController controller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var connected = false;
        var completed = false;
        try
        {
            await controller.ConnectAsync(cancellationToken).ConfigureAwait(false);
            connected = controller.IsConnected && !controller.IsDisposed;
            if (!connected)
            {
                throw new InvalidOperationException("The virtual controller did not enter a connected state.");
            }

            var step = 0;
            foreach (var value in Levels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                controller.SetRightTrigger(value);
                controller.SubmitReport();
                Publish(++step, isLeftTrigger: false, value);
                await Task.Delay(_stepDuration, cancellationToken).ConfigureAwait(false);
            }

            foreach (var value in Levels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                controller.SetLeftTrigger(value);
                controller.SubmitReport();
                Publish(++step, isLeftTrigger: true, value);
                await Task.Delay(_stepDuration, cancellationToken).ConfigureAwait(false);
            }

            completed = true;
        }
        finally
        {
            if (connected && !controller.IsDisposed && controller.IsConnected)
            {
                if (!completed)
                {
                    ResetSafely(controller);
                }

                try
                {
                    await controller.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch when (!completed)
                {
                    // The original cancellation or write failure remains the useful error to report.
                }
            }
        }
    }

    private void Publish(int step, bool isLeftTrigger, byte value)
    {
        var handlers = ProgressChanged;
        if (handlers is null)
        {
            return;
        }

        var progress = new ControllerTestProgress(step, Levels.Length * 2, isLeftTrigger, value);
        foreach (EventHandler<ControllerTestProgress> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, progress);
            }
            catch
            {
                // UI listeners are informational and cannot compromise the safety sequence.
            }
        }
    }

    private static void ResetSafely(IVirtualController controller)
    {
        try
        {
            controller.SetRightTrigger(0);
        }
        catch
        {
        }

        if (controller.IsDisposed || !controller.IsConnected)
        {
            return;
        }

        try
        {
            controller.SetLeftTrigger(0);
        }
        catch
        {
        }

        if (controller.IsDisposed || !controller.IsConnected)
        {
            return;
        }

        try
        {
            controller.SubmitReport();
        }
        catch
        {
        }
    }
}
