using KeyboardAnalogThrottle.App.Services;
using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Emulation;

namespace KeyboardAnalogThrottle.App.Tests.Services;

public sealed class EmulationSessionTests
{
    [Fact]
    public async Task Defers_engine_creation_until_emulation_is_started()
    {
        var engine = new RecordingEngine();
        var factoryCalls = 0;
        await using var session = new EmulationSession(
            () =>
            {
                factoryCalls++;
                return engine;
            },
            new BlockingControllerTestService());

        await session.StopAsync(CancellationToken.None);

        Assert.Equal(0, factoryCalls);

        await session.StartAsync(CancellationToken.None);

        Assert.Equal(1, factoryCalls);
        Assert.Equal(1, engine.StartCalls);
    }

    [Fact]
    public async Task Emergency_reset_cancels_controller_test_and_waits_for_its_cleanup()
    {
        var engine = new RecordingEngine();
        var controllerTest = new BlockingControllerTestService();
        await using var session = new EmulationSession(() => engine, controllerTest);

        await session.StartAsync(CancellationToken.None);

        var test = session.RunControllerTestAsync(CancellationToken.None);
        await controllerTest.Started.Task;

        await session.EmergencyResetAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test);
        Assert.True(controllerTest.CleanupCompleted.Task.IsCompleted);
        Assert.Equal(2, engine.StopCalls);
    }

    [Fact]
    public async Task Emergency_reset_cancels_a_controller_test_waiting_for_an_active_operation()
    {
        var engine = new RecordingEngine { BlockStart = true };
        var controllerTest = new BlockingControllerTestService();
        await using var session = new EmulationSession(() => engine, controllerTest);

        var start = session.StartAsync(CancellationToken.None);
        await engine.Started.Task;
        var test = session.RunControllerTestAsync(CancellationToken.None);
        var reset = session.EmergencyResetAsync(CancellationToken.None);

        engine.AllowStartToFinish();

        try
        {
            await reset.WaitAsync(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test);
            Assert.Equal(0, controllerTest.RunCalls);
        }
        finally
        {
            controllerTest.AllowFinish();
            await start;
            await IgnoreFailureAsync(test);
            await IgnoreFailureAsync(reset);
        }
    }

    [Fact]
    public async Task Controller_test_reports_only_the_diagnostic_controller_as_connected()
    {
        var controllerTest = new BlockingControllerTestService();
        await using var session = new EmulationSession(() => new RecordingEngine(), controllerTest);

        var test = session.RunControllerTestAsync(CancellationToken.None);
        await controllerTest.Started.Task;

        Assert.False(session.State.IsKeyboardHookConnected);
        Assert.True(session.State.IsControllerConnected);

        controllerTest.AllowFinish();
        await test;

        Assert.False(session.State.IsKeyboardHookConnected);
        Assert.False(session.State.IsControllerConnected);
    }

    [Fact]
    public async Task Reconfigure_recreates_a_running_engine_and_restores_emulation()
    {
        var first = new RecordingEngine();
        var second = new RecordingEngine();
        var engines = new Queue<IEmulationEngine>([first, second]);
        await using var session = new EmulationSession(
            () => engines.Dequeue(),
            new BlockingControllerTestService());

        await session.StartAsync(CancellationToken.None);
        await session.ReconfigureAsync(CancellationToken.None);

        Assert.Equal(1, first.StopCalls);
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.StartCalls);
        Assert.True(session.State.IsRunning);
    }

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Preserve the primary assertion failure while allowing deterministic cleanup.
        }
    }

    private sealed class RecordingEngine : IEmulationEngine
    {
        private readonly TaskCompletionSource _startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public EmulationState State { get; set; } = EmulationState.Stopped;

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public bool BlockStart { get; init; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<EmulationState>? StateChanged;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            Started.TrySetResult();
            if (BlockStart)
            {
                await _startGate.Task.WaitAsync(cancellationToken);
            }

            State = State with { IsRunning = true };
            StateChanged?.Invoke(this, State);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            State = State with { IsRunning = false };
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public Task EmergencyResetAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }

        public void AllowStartToFinish() => _startGate.TrySetResult();
    }

    private sealed class BlockingControllerTestService : IControllerTestService
    {
        private readonly TaskCompletionSource _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CleanupCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RunCalls { get; private set; }

        public event EventHandler<ControllerTestProgress>? ProgressChanged;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            RunCalls++;
            ProgressChanged?.Invoke(this, new ControllerTestProgress(1, 12, false, 0));
            Started.TrySetResult();
            try
            {
                await _finish.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                CleanupCompleted.TrySetResult();
            }
        }

        public void AllowFinish() => _finish.TrySetResult();
    }
}
