using KeyboardAnalogThrottle.App.Services;
using KeyboardAnalogThrottle.App.Commands;
using KeyboardAnalogThrottle.App.ViewModels;
using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Core.Input;
using System.ComponentModel;

namespace KeyboardAnalogThrottle.App.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task Command_marshals_can_execute_notifications_to_the_configured_context()
    {
        var context = new RecordingSynchronizationContext();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(() => gate.Task, synchronizationContext: context);
        var notifications = 0;
        command.CanExecuteChanged += (_, _) => notifications++;

        var operation = command.ExecuteAsync(null);
        gate.SetResult();
        await operation;

        Assert.Equal(2, context.PostCount);
        Assert.Equal(0, notifications);
        context.RunPostedCallbacks();
        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task Start_command_is_disabled_while_start_is_in_progress()
    {
        var engine = new BlockingEngine();
        using var viewModel = CreateViewModel(engine);

        var operation = viewModel.StartCommand.ExecuteAsync(null);

        Assert.False(viewModel.StartCommand.CanExecute(null));
        engine.AllowStartToFinish();
        await operation;
    }

    [Fact]
    public async Task Engine_state_projects_status_and_percentages()
    {
        var engine = new BlockingEngine();
        using var viewModel = CreateViewModel(engine);

        engine.Publish(new EmulationState(
            true,
            .125d,
            .25d,
            .5d,
            .75d,
            128,
            191,
            InputHealth.Healthy,
            null));

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Equal("Running", viewModel.Status);
        Assert.Equal(12.5d, viewModel.RawThrottlePercentage);
        Assert.Equal(25d, viewModel.RawBrakePercentage);
        Assert.Equal(50d, viewModel.ThrottlePercentage);
        Assert.Equal(75d, viewModel.BrakePercentage);
        Assert.True(viewModel.IsControllerConnected);
    }

    [Fact]
    public async Task Controller_test_stops_normal_emulation_before_running_the_sequence()
    {
        var engine = new BlockingEngine { State = EmulationState.Stopped with { IsRunning = true } };
        var testService = new RecordingControllerTestService(engine);
        using var viewModel = CreateViewModel(engine, testService);

        await viewModel.TestControllerCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "stop" }, engine.Calls);
        Assert.Equal(new[] { "test" }, testService.Calls);
        Assert.Equal("Controller test complete.", viewModel.ControllerTestStep);
    }

    [Fact]
    public async Task Rapid_engine_snapshots_are_batched_to_the_latest_projection()
    {
        var engine = new BlockingEngine();
        using var viewModel = CreateViewModel(engine);
        var rawThrottleChanges = 0;
        viewModel.PropertyChanged += OnPropertyChanged;

        for (var index = 1; index <= 20; index++)
        {
            engine.Publish(EmulationState.Stopped with { RawThrottle = index / 100d });
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Equal(20d, viewModel.RawThrottlePercentage);
        Assert.Equal(1, rawThrottleChanges);
        return;

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.RawThrottlePercentage))
            {
                rawThrottleChanges++;
            }
        }
    }

    private static MainWindowViewModel CreateViewModel(
        BlockingEngine engine,
        IControllerTestService? testService = null) => new(
        engine,
        new StubConfigurationService(),
        testService ?? new RecordingControllerTestService(engine),
        new StubShellService(),
        synchronizationContext: null);

    private sealed class BlockingEngine : IEmulationEngine
    {
        private readonly TaskCompletionSource _startGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public EmulationState State { get; set; } = EmulationState.Stopped;

        public List<string> Calls { get; } = [];

        public event EventHandler<EmulationState>? StateChanged;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Calls.Add("start");
            await _startGate.Task.WaitAsync(cancellationToken);
            State = State with { IsRunning = true };
            Publish(State);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Calls.Add("stop");
            State = State with { IsRunning = false };
            Publish(State);
            return Task.CompletedTask;
        }

        public Task EmergencyResetAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void AllowStartToFinish() => _startGate.TrySetResult();

        public void Publish(EmulationState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }
    }

    private sealed class RecordingControllerTestService(BlockingEngine engine) : IControllerTestService
    {
        public List<string> Calls { get; } = [];

        public event EventHandler<ControllerTestProgress>? ProgressChanged;

        public Task RunAsync(CancellationToken cancellationToken)
        {
            Calls.Add("test");
            Assert.False(engine.State.IsRunning);
            ProgressChanged?.Invoke(this, new ControllerTestProgress(1, 12, true, 64));
            return Task.CompletedTask;
        }
    }

    private sealed class StubConfigurationService : IConfigurationService
    {
        public AppConfiguration Current { get; } = AppConfiguration.CreateDefault();

        public Task<ConfigurationReloadResult> ReloadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConfigurationReloadResult.Success);

        public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubShellService : IShellService
    {
        public void OpenConfigurationFile() { }

        public void OpenConfigurationFolder() { }

        public void ExitApplication() { }
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = [];

        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            PostCount++;
            _callbacks.Enqueue((callback, state));
        }

        public void RunPostedCallbacks()
        {
            while (_callbacks.TryDequeue(out var callback))
            {
                callback.Callback(callback.State);
            }
        }
    }
}
