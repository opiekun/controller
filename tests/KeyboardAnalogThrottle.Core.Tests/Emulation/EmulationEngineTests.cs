using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Core.Input;
using KeyboardAnalogThrottle.Core.Tests.Fakes;

namespace KeyboardAnalogThrottle.Core.Tests.Emulation;

public sealed class EmulationEngineTests
{
    [Fact]
    public async Task Stop_is_idempotent_and_submits_a_zero_report()
    {
        var controller = new FakeVirtualController();
        await using var engine = CreateEngine(controller, FakeKeyboardInputSource.Pressed(InputKey.W));

        await engine.StartAsync(CancellationToken.None);
        await engine.StopAsync(CancellationToken.None);
        await engine.StopAsync(CancellationToken.None);

        Assert.Equal((byte)0, controller.RightTrigger);
        Assert.Equal((byte)0, controller.LeftTrigger);
        Assert.False(controller.IsConnected);
        Assert.Equal(1, controller.ZeroReportCount);
    }

    [Fact]
    public async Task Controller_update_failure_stops_and_cleans_up_with_a_fault()
    {
        var controller = new FakeVirtualController { SetRightException = new InvalidOperationException("write failed") };
        await using var engine = CreateEngine(controller, FakeKeyboardInputSource.Pressed(InputKey.W));

        await engine.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => !controller.IsConnected);
        await WaitUntilAsync(() => !engine.State.IsRunning);

        Assert.False(engine.State.IsRunning);
        Assert.Equal(EmulationFaultKind.Controller, engine.State.Fault?.Kind);
        Assert.Equal(1, controller.DisconnectCount);
        Assert.True(controller.SetLeftCount > 0);
        Assert.True(controller.SubmitCount > 0);
    }

    [Fact]
    public async Task Unavailable_input_past_the_timeout_stops_as_an_input_fault()
    {
        var clock = new FakeClock();
        var input = FakeKeyboardInputSource.Pressed(InputKey.W);
        input.Health = InputHealth.Unavailable;
        var controller = new FakeVirtualController();
        await using var engine = CreateEngine(controller, input, clock);

        await engine.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => clock.PendingDelayCount == 1);
        clock.Advance(TimeSpan.FromMilliseconds(1_001));
        await WaitUntilAsync(() => !controller.IsConnected);

        Assert.Equal(EmulationFaultKind.InputUnavailable, engine.State.Fault?.Kind);
        Assert.False(input.IsStarted);
    }

    [Fact]
    public async Task Cancelled_start_reverses_a_partially_started_input_source()
    {
        using var cancellation = new CancellationTokenSource();
        var input = FakeKeyboardInputSource.Pressed();
        input.OnStart = cancellation.Cancel;
        var controller = new FakeVirtualController();
        await using var engine = CreateEngine(controller, input);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.StartAsync(cancellation.Token));

        Assert.False(input.IsStarted);
        Assert.Equal(1, input.StopCount);
        Assert.False(controller.IsConnected);
    }

    [Fact]
    public async Task Emergency_reset_stops_and_submits_a_zero_report()
    {
        var controller = new FakeVirtualController();
        await using var engine = CreateEngine(controller, FakeKeyboardInputSource.Pressed(InputKey.W));
        await engine.StartAsync(CancellationToken.None);

        await engine.EmergencyResetAsync(CancellationToken.None);

        Assert.False(engine.State.IsRunning);
        Assert.False(controller.IsConnected);
        Assert.Equal(1, controller.ZeroReportCount);
    }

    [Fact]
    public async Task Does_not_write_after_the_controller_disconnects()
    {
        var clock = new FakeClock();
        var controller = new FakeVirtualController();
        await using var engine = CreateEngine(controller, FakeKeyboardInputSource.Pressed(), clock);
        await engine.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => controller.SubmitCount == 1 && clock.PendingDelayCount == 1);
        controller.ForceDisconnect();
        var writesBeforeFault = (controller.SetRightCount, controller.SetLeftCount, controller.SubmitCount);
        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => !engine.State.IsRunning);

        Assert.Equal(writesBeforeFault, (controller.SetRightCount, controller.SetLeftCount, controller.SubmitCount));
        Assert.Equal(EmulationFaultKind.Controller, engine.State.Fault?.Kind);
    }

    [Fact]
    public async Task Failed_controller_start_stops_the_hook_and_leaves_no_connected_controller()
    {
        var input = FakeKeyboardInputSource.Pressed();
        var controller = new FakeVirtualController { ConnectException = new InvalidOperationException("driver unavailable") };
        await using var engine = CreateEngine(controller, input);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync(CancellationToken.None));

        Assert.False(input.IsStarted);
        Assert.Equal(1, input.StopCount);
        Assert.False(controller.IsConnected);
    }

    [Fact]
    public async Task Unchanged_trigger_bytes_are_not_repeatedly_submitted()
    {
        var clock = new FakeClock();
        var controller = new FakeVirtualController();
        await using var engine = CreateEngine(controller, FakeKeyboardInputSource.Pressed(), clock);
        await engine.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => controller.SubmitCount == 1 && clock.PendingDelayCount == 1);
        clock.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => clock.PendingDelayCount == 1);

        Assert.Equal(1, controller.SubmitCount);
    }

    [Fact]
    public async Task Frame_delta_is_monotonic_and_capped_before_strategy_updates()
    {
        var configuration = AppConfiguration.CreateDefault() with
        {
            Throttle = ChannelConfiguration.CreateThrottleDefault() with { InitialLevel = 0, RiseSeconds = 1 },
            Controller = ControllerConfiguration.Default with { MaximumFrameDeltaMilliseconds = 50 }
        };
        var clock = new FakeClock();
        var controller = new FakeVirtualController();
        await using var engine = CreateEngine(controller, FakeKeyboardInputSource.Pressed(InputKey.W), clock, configuration);
        await engine.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => controller.SubmitCount == 1 && clock.PendingDelayCount == 1);
        clock.Advance(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => engine.State.RawThrottle > 0);

        Assert.Equal(.05d, engine.State.RawThrottle, 6);
    }

    [Fact]
    public async Task Fixed_modifier_level_overrides_ramp_output_without_overwriting_raw_ramp_state()
    {
        var controller = new FakeVirtualController();
        var input = new FakeKeyboardInputSource(new InputSnapshot(
            [InputKey.W, InputKey.LeftShift],
            [new KeyTransition(InputKey.W, true, 1)]));
        await using var engine = CreateEngine(controller, input);

        await engine.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => controller.SubmitCount == 1);

        Assert.Equal(.1d, engine.State.RawThrottle, 6);
        Assert.Equal(.5d, engine.State.Throttle, 6);
        Assert.Equal((byte)128, controller.RightTrigger);
    }

    [Fact]
    public async Task Stop_does_not_attempt_remaining_zero_report_calls_after_disconnect_between_calls()
    {
        var controller = new FakeVirtualController();
        await using var engine = CreateEngine(controller, FakeKeyboardInputSource.Pressed());
        await engine.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => controller.SubmitCount == 1);
        var attemptsBeforeStop = (controller.SetLeftAttemptCount, controller.SubmitAttemptCount);
        controller.OnSetRightTrigger = _ => controller.ForceDisconnect();

        await engine.StopAsync(CancellationToken.None);

        Assert.Equal(attemptsBeforeStop.SetLeftAttemptCount, controller.SetLeftAttemptCount);
        Assert.Equal(attemptsBeforeStop.SubmitAttemptCount, controller.SubmitAttemptCount);
    }

    [Fact]
    public async Task Snapshot_invalid_operation_is_classified_as_an_input_fault()
    {
        var input = FakeKeyboardInputSource.Pressed();
        input.SnapshotException = new InvalidOperationException("hook read failed");
        var controller = new FakeVirtualController();
        await using var engine = CreateEngine(controller, input);

        await engine.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => !engine.State.IsRunning);

        Assert.Equal(EmulationFaultKind.InputUnavailable, engine.State.Fault?.Kind);
    }

    [Fact]
    public async Task Health_object_disposal_is_classified_as_an_input_fault()
    {
        var clock = new FakeClock();
        var input = FakeKeyboardInputSource.Pressed();
        var controller = new FakeVirtualController();
        await using var engine = CreateEngine(controller, input, clock);
        await engine.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => clock.PendingDelayCount == 1);
        input.HealthException = new ObjectDisposedException("keyboard hook");
        clock.Advance(TimeSpan.FromSeconds(1));

        await WaitUntilAsync(() => !engine.State.IsRunning);

        Assert.Equal(EmulationFaultKind.InputUnavailable, engine.State.Fault?.Kind);
    }

    private static IEmulationEngine CreateEngine(
        IVirtualController controller,
        IKeyboardInputSource input,
        FakeClock? clock = null,
        AppConfiguration? configuration = null) =>
        new EmulationEngine(
            configuration ?? AppConfiguration.CreateDefault(),
            controller,
            input,
            clock ?? new FakeClock());

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
            {
                throw new TimeoutException("Condition was not reached.");
            }

            await Task.Yield();
        }
    }
}
