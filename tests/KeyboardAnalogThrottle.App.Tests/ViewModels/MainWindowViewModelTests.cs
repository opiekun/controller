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
        Assert.False(viewModel.SaveShortcutsCommand.CanExecute(null));
        engine.AllowStartToFinish();
        await operation;
    }

    [Fact]
    public async Task Save_shortcuts_command_saves_the_editor_candidate_and_reports_success()
    {
        var engine = new BlockingEngine();
        var configuration = new RecordingConfigurationService(AppConfiguration.CreateDefault());
        using var viewModel = new MainWindowViewModel(
            new TestSession(engine, new RecordingControllerTestService(engine)),
            configuration,
            new StubShellService(),
            synchronizationContext: null);
        viewModel.ShortcutEditor.ThrottlePrimaryBinding = "T";

        await viewModel.SaveShortcutsCommand.ExecuteAsync(null);

        var saved = Assert.Single(configuration.SavedConfigurations);
        Assert.Equal("T", saved.Throttle.PrimaryBinding);
        Assert.Equal("Shortcuts saved and applied.", viewModel.StatusMessage);
        Assert.Empty(viewModel.LastError);
    }

    [Fact]
    public async Task Save_shortcuts_command_projects_editor_validation_without_saving_or_discarding_field()
    {
        var engine = new BlockingEngine();
        var configuration = new RecordingConfigurationService(AppConfiguration.CreateDefault());
        using var viewModel = new MainWindowViewModel(
            new TestSession(engine, new RecordingControllerTestService(engine)),
            configuration,
            new StubShellService(),
            synchronizationContext: null);
        viewModel.ShortcutEditor.ThrottlePrimaryBinding = "invalid binding";

        await viewModel.SaveShortcutsCommand.ExecuteAsync(null);

        Assert.Equal("invalid binding", viewModel.ShortcutEditor.ThrottlePrimaryBinding);
        Assert.Equal("Binding 'invalid binding' is invalid.", viewModel.LastError);
        Assert.Empty(configuration.SavedConfigurations);
    }

    [Fact]
    public void Stopped_state_projects_hook_and_controller_as_disconnected()
    {
        var engine = new BlockingEngine();
        using var viewModel = CreateViewModel(engine);

        Assert.False(viewModel.IsKeyboardHookConnected);
        Assert.False(viewModel.IsControllerConnected);
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
            null,
            true,
            true));

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Equal("Running", viewModel.Status);
        Assert.Equal(12.5d, viewModel.RawThrottlePercentage);
        Assert.Equal(25d, viewModel.RawBrakePercentage);
        Assert.Equal(50d, viewModel.ThrottlePercentage);
        Assert.Equal(75d, viewModel.BrakePercentage);
        Assert.True(viewModel.IsKeyboardHookConnected);
        Assert.True(viewModel.IsControllerConnected);
    }

    [Fact]
    public async Task Fault_state_projects_hook_and_controller_as_disconnected()
    {
        var engine = new BlockingEngine();
        using var viewModel = CreateViewModel(engine);
        engine.Publish(EmulationState.Stopped with
        {
            IsRunning = false,
            IsKeyboardHookConnected = false,
            IsControllerConnected = false,
            Fault = new EmulationFault(EmulationFaultKind.Controller, "controller lost")
        });

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Equal("Faulted", viewModel.Status);
        Assert.False(viewModel.IsKeyboardHookConnected);
        Assert.False(viewModel.IsControllerConnected);
    }

    [Fact]
    public async Task Dashboard_projects_live_engine_diagnostics_instead_of_static_configuration_text()
    {
        var engine = new BlockingEngine();
        using var viewModel = CreateViewModel(engine);
        engine.Publish(EmulationState.Stopped with
        {
            IsRunning = true,
            IsKeyboardHookConnected = true,
            IsControllerConnected = true,
            ControllerAvailability = VirtualControllerAvailability.Available,
            ActiveThrottleBinding = "Ctrl+Shift+W (fixed)",
            ActiveBrakeBinding = "S (ratchet increase)",
            IsThrottleCutActive = true,
            IsInputSuppressionEnabled = true
        });

        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.Equal("Available", viewModel.VigemBusAvailability);
        Assert.Equal("Ctrl+Shift+W (fixed)", viewModel.ActiveThrottleBinding);
        Assert.Equal("S (ratchet increase)", viewModel.ActiveBrakeBinding);
        Assert.True(viewModel.IsThrottleCutActive);
        Assert.True(viewModel.IsSuppressionEnabled);
        Assert.DoesNotContain("Emergency:", viewModel.ActiveBindings);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Safety_reset_remains_available_while_controller_test_is_running(bool useResetCommand)
    {
        var controllerTest = new CancellableControllerTestService();
        await using var session = new EmulationSession(() => new BlockingEngine(), controllerTest);
        using var viewModel = new MainWindowViewModel(
            session,
            new StubConfigurationService(),
            new StubShellService(),
            synchronizationContext: null);

        var testOperation = viewModel.TestControllerCommand.ExecuteAsync(null);
        await controllerTest.Started.Task;
        var resetCommand = useResetCommand ? viewModel.ResetCommand : viewModel.EmergencyStopCommand;

        Assert.True(resetCommand.CanExecute(null));

        await resetCommand.ExecuteAsync(null);
        await testOperation;
        Assert.True(controllerTest.CleanupCompleted.Task.IsCompleted);
        Assert.Equal("Controller test cancelled.", viewModel.ControllerTestStep);
        Assert.Empty(viewModel.LastError);
    }

    [Fact]
    public async Task Exit_cancels_controller_test_and_waits_for_cleanup_before_closing()
    {
        var controllerTest = new CancellableControllerTestService();
        var shell = new RecordingShellService(() => controllerTest.CleanupCompleted.Task.IsCompleted);
        await using var session = new EmulationSession(() => new BlockingEngine(), controllerTest);
        using var viewModel = new MainWindowViewModel(
            session,
            new StubConfigurationService(),
            shell,
            synchronizationContext: null);

        var testOperation = viewModel.TestControllerCommand.ExecuteAsync(null);
        await controllerTest.Started.Task;

        Assert.True(viewModel.ExitCommand.CanExecute(null));

        await viewModel.ExitCommand.ExecuteAsync(null);
        await testOperation;
        await shell.Exited.Task;

        Assert.True(controllerTest.CleanupCompleted.Task.IsCompleted);
        Assert.True(shell.ExitCalled);
        Assert.True(shell.TestCleanupCompletedAtExit);
        Assert.Equal("Controller test cancelled.", viewModel.ControllerTestStep);
        Assert.Empty(viewModel.LastError);
    }

    [Fact]
    public void Stopped_dashboard_diagnostics_do_not_claim_configured_bindings_are_active()
    {
        var engine = new BlockingEngine();
        using var viewModel = CreateViewModel(engine);

        Assert.Equal("Throttle: None | Brake: None", viewModel.ActiveBindings);
        Assert.False(viewModel.IsThrottleCutActive);
        Assert.False(viewModel.IsSuppressionEnabled);
        Assert.Equal("Unknown", viewModel.VigemBusAvailability);
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
        new TestSession(engine, testService ?? new RecordingControllerTestService(engine)),
        new StubConfigurationService(),
        new StubShellService(),
        synchronizationContext: null);

    private sealed class TestSession : IEmulationSession
    {
        private readonly BlockingEngine _engine;
        private readonly IControllerTestService _controllerTest;

        public TestSession(BlockingEngine engine, IControllerTestService controllerTest)
        {
            _engine = engine;
            _controllerTest = controllerTest;
            _engine.StateChanged += OnStateChanged;
            _controllerTest.ProgressChanged += OnProgressChanged;
        }

        public EmulationState State => _engine.State;

        public event EventHandler<EmulationState>? StateChanged;

        public event EventHandler<ControllerTestProgress>? ControllerTestProgressChanged;

        public Task StartAsync(CancellationToken cancellationToken) => _engine.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => _engine.StopAsync(cancellationToken);

        public Task EmergencyResetAsync(CancellationToken cancellationToken) => _engine.EmergencyResetAsync(cancellationToken);

        public Task ReconfigureAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task RunControllerTestAsync(CancellationToken cancellationToken)
        {
            if (_engine.State.IsRunning)
            {
                await _engine.StopAsync(cancellationToken);
            }

            await _controllerTest.RunAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            _engine.StateChanged -= OnStateChanged;
            _controllerTest.ProgressChanged -= OnProgressChanged;
            return ValueTask.CompletedTask;
        }

        private void OnStateChanged(object? sender, EmulationState state) => StateChanged?.Invoke(this, state);

        private void OnProgressChanged(object? sender, ControllerTestProgress progress) =>
            ControllerTestProgressChanged?.Invoke(this, progress);
    }

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

    private sealed class CancellableControllerTestService : IControllerTestService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CleanupCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler<ControllerTestProgress>? ProgressChanged
        {
            add { }
            remove { }
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                CleanupCompleted.TrySetResult();
            }
        }
    }

    private sealed class StubConfigurationService : IConfigurationService
    {
        public AppConfiguration Current { get; } = AppConfiguration.CreateDefault();

        public event Func<AppConfiguration, CancellationToken, Task>? ConfigurationChanged
        {
            add { }
            remove { }
        }

        public Task<ConfigurationReloadResult> ReloadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConfigurationReloadResult.Success);

        public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateAsync(Func<AppConfiguration, AppConfiguration> update, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingConfigurationService(AppConfiguration current) : IConfigurationService
    {
        public AppConfiguration Current { get; } = current;

        public List<AppConfiguration> SavedConfigurations { get; } = [];

        public event Func<AppConfiguration, CancellationToken, Task>? ConfigurationChanged
        {
            add { }
            remove { }
        }

        public Task<ConfigurationReloadResult> ReloadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConfigurationReloadResult.Success);

        public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken)
        {
            SavedConfigurations.Add(configuration);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Func<AppConfiguration, AppConfiguration> update, CancellationToken cancellationToken) =>
            SaveAsync(update(Current), cancellationToken);
    }

    private sealed class StubShellService : IShellService
    {
        public void OpenConfigurationFile() { }

        public void OpenConfigurationFolder() { }

        public void ExitApplication() { }
    }

    private sealed class RecordingShellService(Func<bool> isTestCleanupComplete) : IShellService
    {
        public TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ExitCalled { get; private set; }

        public bool TestCleanupCompletedAtExit { get; private set; }

        public void OpenConfigurationFile() { }

        public void OpenConfigurationFolder() { }

        public void ExitApplication()
        {
            TestCleanupCompletedAtExit = isTestCleanupComplete();
            ExitCalled = true;
            Exited.TrySetResult();
        }
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
