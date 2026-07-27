using KeyboardAnalogThrottle.App.Commands;
using KeyboardAnalogThrottle.App.Services;
using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Infrastructure.Windows.Controller;
using Microsoft.Extensions.Logging;

namespace KeyboardAnalogThrottle.App.ViewModels;

/// <summary>
/// Presents engine state and serializes user initiated operations for the dashboard.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan StateRefreshInterval = TimeSpan.FromSeconds(1d / 30d);

    private readonly IEmulationSession _session;
    private readonly IConfigurationService _configuration;
    private readonly IShellService _shell;
    private readonly ILogger<MainWindowViewModel>? _logger;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly object _stateGate = new();
    private EmulationState? _pendingState;
    private bool _stateRefreshScheduled;
    private bool _isOperationInProgress;
    private bool _isEmergencyResetInProgress;
    private bool _isControllerTestRunning;
    private bool _disposed;
    private string _status = "Stopped";
    private string _statusMessage = "Ready.";
    private string _lastError = string.Empty;
    private string _controllerTestStep = "Not running.";
    private double _rawThrottlePercentage;
    private double _rawBrakePercentage;
    private double _throttlePercentage;
    private double _brakePercentage;
    private bool _isKeyboardHookConnected;
    private bool _isControllerConnected;
    private bool _isSuppressionEnabled;
    private string _activeBindings = string.Empty;
    private string _inputHealth = "Healthy";

    public MainWindowViewModel(
        IEmulationSession session,
        IConfigurationService configuration,
        IShellService shell,
        ILogger<MainWindowViewModel>? logger = null,
        SynchronizationContext? synchronizationContext = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _logger = logger;
        _synchronizationContext = synchronizationContext ?? SynchronizationContext.Current;

        StartCommand = CreateCommand(StartAsync, CanStart);
        StopCommand = CreateCommand(StopAsync, CanStop);
        EmergencyStopCommand = CreateCommand(EmergencyStopAsync, CanEmergencyReset);
        ResetCommand = CreateCommand(EmergencyStopAsync, CanEmergencyReset);
        ReloadConfigurationCommand = CreateCommand(ReloadConfigurationAsync, CanOperate);
        OpenConfigurationFolderCommand = CreateCommand(OpenConfigurationFolderAsync, CanOperate);
        OpenConfigurationFileCommand = CreateCommand(OpenConfigurationFileAsync, CanOperate);
        TestControllerCommand = CreateCommand(RunControllerTestAsync, CanOperate);
        ExitCommand = CreateCommand(ExitAsync, CanExit);

        _session.StateChanged += OnEngineStateChanged;
        _session.ControllerTestProgressChanged += OnControllerTestProgressChanged;
        ApplyConfiguration(_configuration.Current);
        ApplyState(_session.State);
    }

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand EmergencyStopCommand { get; }
    public AsyncRelayCommand ResetCommand { get; }
    public AsyncRelayCommand ReloadConfigurationCommand { get; }
    public AsyncRelayCommand OpenConfigurationFolderCommand { get; }
    public AsyncRelayCommand OpenConfigurationFileCommand { get; }
    public AsyncRelayCommand TestControllerCommand { get; }
    public AsyncRelayCommand ExitCommand { get; }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string LastError { get => _lastError; private set => SetProperty(ref _lastError, value); }
    public string ControllerTestStep { get => _controllerTestStep; private set => SetProperty(ref _controllerTestStep, value); }
    public double RawThrottlePercentage { get => _rawThrottlePercentage; private set => SetProperty(ref _rawThrottlePercentage, value); }
    public double RawBrakePercentage { get => _rawBrakePercentage; private set => SetProperty(ref _rawBrakePercentage, value); }
    public double ThrottlePercentage { get => _throttlePercentage; private set => SetProperty(ref _throttlePercentage, value); }
    public double BrakePercentage { get => _brakePercentage; private set => SetProperty(ref _brakePercentage, value); }
    public bool IsKeyboardHookConnected { get => _isKeyboardHookConnected; private set => SetProperty(ref _isKeyboardHookConnected, value); }
    public bool IsControllerConnected { get => _isControllerConnected; private set => SetProperty(ref _isControllerConnected, value); }
    public bool IsSuppressionEnabled { get => _isSuppressionEnabled; private set => SetProperty(ref _isSuppressionEnabled, value); }
    public string ActiveBindings { get => _activeBindings; private set => SetProperty(ref _activeBindings, value); }
    public string InputHealth { get => _inputHealth; private set => SetProperty(ref _inputHealth, value); }
    public bool IsOperationInProgress { get => _isOperationInProgress; private set => SetProperty(ref _isOperationInProgress, value); }
    public bool IsControllerTestRunning { get => _isControllerTestRunning; private set => SetProperty(ref _isControllerTestRunning, value); }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.StateChanged -= OnEngineStateChanged;
        _session.ControllerTestProgressChanged -= OnControllerTestProgressChanged;
    }

    private bool CanOperate() => !_disposed && !IsOperationInProgress && !_isEmergencyResetInProgress;
    private bool CanStart() => CanOperate() && !_session.State.IsRunning;
    private bool CanStop() => CanOperate() && _session.State.IsRunning;
    private bool CanEmergencyReset() => !_disposed && !_isEmergencyResetInProgress;
    private bool CanExit() => !_disposed && !_isEmergencyResetInProgress;

    private Task StartAsync() => ExecuteOperationAsync(
        () => _session.StartAsync(CancellationToken.None),
        "Emulation started.");

    private Task StopAsync() => ExecuteOperationAsync(
        () => _session.StopAsync(CancellationToken.None),
        "Emulation stopped.");

    private Task EmergencyStopAsync() => ExecuteEmergencyResetAsync(exitApplication: false);

    private async Task ReloadConfigurationAsync()
    {
        await ExecuteOperationAsync(async () =>
        {
            var result = await _configuration.ReloadAsync(CancellationToken.None);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors.Select(static error => $"{error.PropertyName}: {error.Message}")));
            }

            ApplyOnUiThread(() => ApplyConfiguration(_configuration.Current));
        }, "Configuration reloaded. Restart the application to apply changes.");
    }

    private Task OpenConfigurationFolderAsync() => ExecuteOperationAsync(() =>
    {
        _shell.OpenConfigurationFolder();
        return Task.CompletedTask;
    }, "Configuration folder opened.");

    private Task OpenConfigurationFileAsync() => ExecuteOperationAsync(() =>
    {
        _shell.OpenConfigurationFile();
        return Task.CompletedTask;
    }, "Configuration file opened.");

    private async Task RunControllerTestAsync()
    {
        await ExecuteOperationAsync(async () =>
        {
            IsControllerTestRunning = true;
            ControllerTestStep = "Preparing controller test.";
            await _session.RunControllerTestAsync(CancellationToken.None);
            ControllerTestStep = "Controller test complete.";
        }, "Controller test completed.", () =>
        {
            ControllerTestStep = "Controller test cancelled.";
            StatusMessage = "Controller test cancelled.";
        });
    }

    private Task ExitAsync() => ExecuteEmergencyResetAsync(exitApplication: true);

    private async Task ExecuteEmergencyResetAsync(bool exitApplication)
    {
        _isEmergencyResetInProgress = true;
        RaiseCommandCanExecuteChanged();
        LastError = string.Empty;
        try
        {
            await _session.EmergencyResetAsync(CancellationToken.None);
            ApplyOnUiThread(() => StatusMessage = "Emergency reset completed.");
            if (exitApplication)
            {
                ApplyOnUiThread(_shell.ExitApplication);
            }
        }
        catch (Exception exception)
        {
            ReportOperationFailure(exception);
        }
        finally
        {
            ApplyOnUiThread(() =>
            {
                _isEmergencyResetInProgress = false;
                RaiseCommandCanExecuteChanged();
            });
        }
    }

    private async Task ExecuteOperationAsync(
        Func<Task> operation,
        string successMessage,
        Action? cancellationHandler = null)
    {
        IsOperationInProgress = true;
        RaiseCommandCanExecuteChanged();
        LastError = string.Empty;
        try
        {
            await operation();
            ApplyOnUiThread(() => StatusMessage = successMessage);
        }
        catch (OperationCanceledException) when (cancellationHandler is not null)
        {
            ApplyOnUiThread(cancellationHandler);
        }
        catch (Exception exception)
        {
            ReportOperationFailure(exception);
        }
        finally
        {
            ApplyOnUiThread(() =>
            {
                IsControllerTestRunning = false;
                IsOperationInProgress = false;
                RaiseCommandCanExecuteChanged();
            });
        }
    }

    private void OnEngineStateChanged(object? sender, EmulationState state)
    {
        lock (_stateGate)
        {
            _pendingState = state;
            if (_stateRefreshScheduled || _disposed)
            {
                return;
            }

            _stateRefreshScheduled = true;
        }

        _ = PublishPendingStateAsync();
    }

    private async Task PublishPendingStateAsync()
    {
        await Task.Delay(StateRefreshInterval).ConfigureAwait(false);
        EmulationState? state;
        lock (_stateGate)
        {
            state = _pendingState;
            _pendingState = null;
            _stateRefreshScheduled = false;
        }

        if (state is not null && !_disposed)
        {
            ApplyOnUiThread(() => ApplyState(state));
        }
    }

    private void OnControllerTestProgressChanged(object? sender, ControllerTestProgress progress) =>
        ApplyOnUiThread(() => ControllerTestStep = $"Step {progress.Step} of {progress.TotalSteps}: {(progress.IsLeftTrigger ? "left" : "right")} trigger {progress.Value}." );

    private void ApplyState(EmulationState state)
    {
        Status = state.Fault is not null ? "Faulted" : state.IsRunning ? "Running" : "Stopped";
        RawThrottlePercentage = ToPercentage(state.RawThrottle);
        RawBrakePercentage = ToPercentage(state.RawBrake);
        ThrottlePercentage = ToPercentage(state.Throttle);
        BrakePercentage = ToPercentage(state.Brake);
        IsKeyboardHookConnected = state.IsKeyboardHookConnected;
        IsControllerConnected = state.IsControllerConnected;
        InputHealth = state.InputHealth.ToString();
        if (state.Fault is not null)
        {
            LastError = state.Fault.Message;
        }

        RaiseCommandCanExecuteChanged();
    }

    private void ApplyConfiguration(AppConfiguration configuration)
    {
        IsSuppressionEnabled = configuration.Input.SuppressMappedKeys;
        ActiveBindings = $"Throttle: {configuration.Throttle.PrimaryBinding}; fixed: {FormatFixedLevels(configuration.Throttle)} | " +
            $"Brake: {configuration.Brake.PrimaryBinding}; fixed: {FormatFixedLevels(configuration.Brake)} | " +
            $"Ratchet: increase {configuration.Ratchet.IncreaseBinding}, decrease {configuration.Ratchet.DecreaseBinding}, reset {configuration.Ratchet.ResetBinding} | " +
            $"Throttle cut: {configuration.Input.ThrottleCutBinding} | Emergency: {configuration.Input.EmergencyDisableBinding}";
    }

    private void ReportOperationFailure(Exception exception)
    {
        _logger?.LogError(exception, "Dashboard operation failed.");
        ApplyOnUiThread(() =>
        {
            LastError = exception.Message;
            StatusMessage = exception is VigemDriverException
                ? "Virtual controller driver unavailable. Install and enable ViGEmBus, then retry."
                : "Operation failed. Review the error details.";
        });
    }

    private void ApplyOnUiThread(Action action)
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
        {
            action();
            return;
        }

        _synchronizationContext.Post(static state => ((Action)state!).Invoke(), action);
    }

    private void RaiseCommandCanExecuteChanged()
    {
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        EmergencyStopCommand.RaiseCanExecuteChanged();
        ResetCommand.RaiseCanExecuteChanged();
        ReloadConfigurationCommand.RaiseCanExecuteChanged();
        OpenConfigurationFolderCommand.RaiseCanExecuteChanged();
        OpenConfigurationFileCommand.RaiseCanExecuteChanged();
        TestControllerCommand.RaiseCanExecuteChanged();
        ExitCommand.RaiseCanExecuteChanged();
    }

    private AsyncRelayCommand CreateCommand(Func<Task> execute, Func<bool> canExecute) =>
        new(execute, canExecute, _synchronizationContext);

    private static string FormatFixedLevels(ChannelConfiguration channel) => channel.FixedLevels.Count == 0
        ? "none"
        : string.Join(", ", channel.FixedLevels
            .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static entry => $"{entry.Key} ({entry.Value:P0})"));

    private static double ToPercentage(double value) => Math.Round(Math.Clamp(value, 0d, 1d) * 100d, 1);
}
