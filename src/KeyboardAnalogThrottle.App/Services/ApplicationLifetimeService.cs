using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Infrastructure.Windows.Lifecycle;
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
    private int _initialized;
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
        _logger.LogInformation(
            "Application startup. Runtime {RuntimeVersion}; operating system {OperatingSystem}; configuration path {ConfigurationPath}.",
            Environment.Version,
            Environment.OSVersion,
            JsonConfigurationService.GetDefaultConfigurationPath());

        var result = await _configuration.ReloadAsync(cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            foreach (var error in result.Errors)
            {
                _logger.LogError("Configuration error for {PropertyName}: {Message}", error.PropertyName, error.Message);
            }

            return result;
        }

        _windowsLifecycle.Start();
        Volatile.Write(ref _initialized, 1);
        _logger.LogInformation("Configuration loaded successfully.");
        return result;
    }

    public void RequestEmergencyStop()
    {
        if (Volatile.Read(ref _initialized) == 0)
        {
            return;
        }

        _logger.LogWarning("Emergency emulation cleanup requested.");

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
        _logger.LogInformation("Application shutdown requested.");
        var initialized = Volatile.Read(ref _initialized) != 0;
        if (initialized)
        {
            RequestEmergencyStop();
        }

        Task? cleanup;
        lock (_cleanupGate)
        {
            cleanup = _cleanup;
        }

        if (cleanup is not null)
        {
            await cleanup.ConfigureAwait(false);
        }

        if (initialized && GetSession() is { } session)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        _logger.LogInformation("Application shutdown completed.");
    }

    private async Task EmergencyStopAsync()
    {
        var session = GetSession();
        if (session is null)
        {
            return;
        }

        try
        {
            await session.EmergencyResetAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Emergency emulation cleanup failed.");
        }
    }

    private IEmulationSession? GetSession() => _services.GetService(typeof(IEmulationSession)) as IEmulationSession;

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
