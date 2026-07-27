using KeyboardAnalogThrottle.App.Services;
using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Infrastructure.Windows.Controller;

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
    public async Task Driver_creation_failure_publishes_unavailable_controller_fault_state()
    {
        await using var session = new EmulationSession(
            () => throw new VigemDriverException(new InvalidOperationException("driver unavailable")),
            new BlockingControllerTestService());

        await Assert.ThrowsAsync<VigemDriverException>(() => session.StartAsync(CancellationToken.None));

        Assert.Equal(VirtualControllerAvailability.Unavailable, session.State.ControllerAvailability);
        Assert.Equal(EmulationFaultKind.Controller, session.State.Fault?.Kind);
        Assert.False(session.State.IsControllerConnected);
        Assert.False(session.State.IsKeyboardHookConnected);
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

    [Fact]
    public async Task Failed_reconfigure_restores_the_previous_running_engine()
    {
        var initial = AppConfiguration.CreateDefault();
        var configuration = new TestConfigurationService(initial);
        var first = new RecordingEngine();
        var failedCandidate = new RecordingEngine { StartException = new InvalidOperationException("candidate start failed") };
        var engines = new Queue<IEmulationEngine>([first, failedCandidate]);
        await using var session = new EmulationSession(
            _ => engines.Dequeue(),
            new BlockingControllerTestService(),
            configuration: configuration);

        await session.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => configuration.PublishAsync(
            initial with { Controller = initial.Controller with { UpdateRateHz = 144 } }));

        Assert.Equal(2, first.StartCalls);
        Assert.Equal(1, first.StopCalls);
        Assert.Equal(0, first.DisposeCalls);
        Assert.Equal(1, failedCandidate.StartCalls);
        Assert.Equal(1, failedCandidate.DisposeCalls);
        Assert.True(session.State.IsRunning);
    }

    [Fact]
    public async Task Failed_candidate_cleanup_does_not_prevent_previous_engine_rollback()
    {
        var initial = AppConfiguration.CreateDefault();
        var configuration = new TestConfigurationService(initial);
        var first = new RecordingEngine();
        var failedCandidate = new RecordingEngine
        {
            StartException = new InvalidOperationException("candidate start failed"),
            DisposeException = new InvalidOperationException("candidate dispose failed")
        };
        var engines = new Queue<IEmulationEngine>([first, failedCandidate]);
        await using var session = new EmulationSession(
            _ => engines.Dequeue(),
            new BlockingControllerTestService(),
            configuration: configuration);

        await session.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => configuration.PublishAsync(
            initial with { Controller = initial.Controller with { UpdateRateHz = 144 } }));

        Assert.Equal(2, first.StartCalls);
        Assert.True(session.State.IsRunning);
    }

    [Fact]
    public async Task Failed_previous_disposal_rolls_back_the_newly_started_candidate()
    {
        var initial = AppConfiguration.CreateDefault();
        var configuration = new TestConfigurationService(initial);
        var first = new RecordingEngine
        {
            DisposeException = new InvalidOperationException("previous dispose failed"),
            ThrowDisposeOnce = true
        };
        var candidate = new RecordingEngine();
        var engines = new Queue<IEmulationEngine>([first, candidate]);
        await using var session = new EmulationSession(
            _ => engines.Dequeue(),
            new BlockingControllerTestService(),
            configuration: configuration);

        await session.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => configuration.PublishAsync(
            initial with { Controller = initial.Controller with { UpdateRateHz = 144 } }));

        Assert.Equal(2, first.StartCalls);
        Assert.Equal(1, candidate.DisposeCalls);
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

        public Exception? StartException { get; init; }

        public Exception? DisposeException { get; init; }

        public bool ThrowDisposeOnce { get; init; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<EmulationState>? StateChanged;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            StartCalls++;
            Started.TrySetResult();
            if (StartException is not null)
            {
                throw StartException;
            }
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
            if (DisposeException is not null && (!ThrowDisposeOnce || DisposeCalls == 1))
            {
                throw DisposeException;
            }

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

    private sealed class TestConfigurationService(AppConfiguration current) : IConfigurationService
    {
        public AppConfiguration Current { get; private set; } = current;

        public event Func<AppConfiguration, CancellationToken, Task>? ConfigurationChanged;

        public async Task PublishAsync(AppConfiguration candidate)
        {
            var handlers = ConfigurationChanged;
            if (handlers is not null)
            {
                foreach (Func<AppConfiguration, CancellationToken, Task> handler in handlers.GetInvocationList())
                {
                    await handler(candidate, CancellationToken.None);
                }
            }

            Current = candidate;
        }

        public Task<ConfigurationReloadResult> ReloadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConfigurationReloadResult.Success);

        public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
