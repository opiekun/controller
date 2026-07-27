using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Core.Tests.Fakes;

namespace KeyboardAnalogThrottle.Core.Tests.Emulation;

public sealed class ControllerTestSequenceTests
{
    [Fact]
    public async Task Test_sequence_sends_each_trigger_through_the_documented_levels()
    {
        var controller = new FakeVirtualController();

        await new ControllerTestSequence(TimeSpan.Zero).RunAsync(controller, CancellationToken.None);

        var expected = new byte[] { 0, 64, 128, 191, 255, 0 };
        Assert.Equal(expected, controller.RightTriggerValues);
        Assert.Equal(expected, controller.LeftTriggerValues);
        Assert.Equal((byte)0, controller.RightTrigger);
        Assert.Equal((byte)0, controller.LeftTrigger);
        Assert.False(controller.IsConnected);
    }

    [Fact]
    public async Task Cancellation_resets_both_triggers_before_disconnecting()
    {
        using var cancellation = new CancellationTokenSource();
        var controller = new FakeVirtualController
        {
            OnSetRightTrigger = value =>
            {
                if (value == 64)
                {
                    cancellation.Cancel();
                }
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ControllerTestSequence(TimeSpan.FromSeconds(1)).RunAsync(controller, cancellation.Token));

        Assert.Equal((byte)0, controller.RightTrigger);
        Assert.Equal((byte)0, controller.LeftTrigger);
        Assert.True(controller.ZeroReportCount > 0);
        Assert.False(controller.IsConnected);
    }

    [Fact]
    public async Task Write_failure_resets_both_triggers_before_disconnecting()
    {
        var failOnce = true;
        var controller = new FakeVirtualController
        {
            OnSetRightTrigger = value =>
            {
                if (value == 64 && failOnce)
                {
                    failOnce = false;
                    throw new InvalidOperationException("simulated write failure");
                }
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ControllerTestSequence(TimeSpan.Zero).RunAsync(controller, CancellationToken.None));

        Assert.Equal((byte)0, controller.RightTrigger);
        Assert.Equal((byte)0, controller.LeftTrigger);
        Assert.True(controller.ZeroReportCount > 0);
        Assert.False(controller.IsConnected);
    }

    [Fact]
    public async Task Test_sequence_publishes_progress_for_each_trigger_level()
    {
        var controller = new FakeVirtualController();
        var progress = new List<ControllerTestProgress>();
        var sequence = new ControllerTestSequence(TimeSpan.Zero);
        sequence.ProgressChanged += (_, update) => progress.Add(update);

        await sequence.RunAsync(controller, CancellationToken.None);

        Assert.Equal(12, progress.Count);
        Assert.Equal(new ControllerTestProgress(1, 12, false, 0), progress[0]);
        Assert.Equal(new ControllerTestProgress(12, 12, true, 0), progress[^1]);
    }
}
