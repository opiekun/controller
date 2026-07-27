using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;
using KeyboardAnalogThrottle.Infrastructure.Windows.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyboardAnalogThrottle.App.Services;

/// <summary>
/// Coordinates startup validation and routes process and Windows lifecycle notifications through one best-effort stop operation.
/// </summary>
public sealed class ApplicationLifetimeService : IAsyncDisposable
{
    private readonly IConfigurationService _configuration;
    private readonly IServiceProvider _services;
    private readonly ILogger<ApplicationLifetimeService> _logger;
    private readonly WindowsLifecycleMonitor _windowsLifecycle;
    private readonly object _cleanupGate = new();
    private Task? _cleanup;
    private IEmulationEngine? _engine;
    private LowLevelKeyboardInputSource? _input;
    private int _disposed;

    public ApplicationLifetimeService(
        IConfigurationService configuration,
        IServiceProvider services,
        ILogger<ApplicationLifetimeService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _windowsLifecycle = new WindowsLifecycleMonitor(RequestEmergencyStop);
    }

    public async Task<ConfigurationReloadResult> InitializeAsync(CancellationToken cancellationToken)
    {
        var result = await _configuration.ReloadAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            foreach (var error in result.Errors)
            {
                _logger.LogError("Configuration error for {PropertyName}: {Message}", error.PropertyName, error.Message);
            }

            return result;
        }

        var engine = _services.GetRequiredService<IEmulationEngine>();
        _input = _services.GetRequiredService<LowLevelKeyboardInputSource>();
        _engine = engine;
        engine.StateChanged += OnEngineStateChanged;
        _windowsLifecycle.Start();
        return result;
    }

    public void RequestEmergencyStop()
    {
        Task cleanup;
        lock (_cleanupGate)
        {
            if (_cleanup is { IsCompleted: false })
            {
                return;
            }

            cleanup = EmergencyStopAsync();
            _cleanup = cleanup;
        }

        _ = cleanup.ContinueWith(
            completedCleanup => ClearCompletedCleanup(completedCleanup),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _windowsLifecycle.Dispose();
        var engine = _engine;
        if (engine is not null)
        {
            engine.StateChanged -= OnEngineStateChanged;
        }

        RequestEmergencyStop();
        Task? cleanup;
        lock (_cleanupGate)
        {
            cleanup = _cleanup;
        }

        if (cleanup is not null)
        {
            await cleanup.ConfigureAwait(false);
        }

        if (engine is not null)
        {
            await engine.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task EmergencyStopAsync()
    {
        var engine = _engine;
        var input = _input;
        if (engine is null || input is null)
        {
            return;
        }

        try
        {
            input.SetEngineRunning(isRunning: false);
            await engine.EmergencyResetAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Emergency emulation cleanup failed.");
        }
    }

    private void OnEngineStateChanged(object? sender, KeyboardAnalogThrottle.Core.Emulation.EmulationState state) => _input?.SetEngineRunning(state.IsRunning);

    private void ClearCompletedCleanup(Task completedCleanup)
    {
        lock (_cleanupGate)
        {
            if (ReferenceEquals(_cleanup, completedCleanup))
            {
                _cleanup = null;
            }
        }
    }
}
