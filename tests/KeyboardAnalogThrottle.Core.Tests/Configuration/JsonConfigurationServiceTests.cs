using KeyboardAnalogThrottle.App.Services;
using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Infrastructure.Windows.Lifecycle;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyboardAnalogThrottle.Core.Tests.Configuration;

public sealed class JsonConfigurationServiceTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "KeyboardAnalogThrottle.Tests", Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_directory, "config.json");

    [Fact]
    public async Task Reload_creates_a_valid_default_file_only_when_the_file_is_absent()
    {
        using var service = new JsonConfigurationService(ConfigPath);

        var result = await service.ReloadAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(ConfigPath));
        Assert.Empty(ConfigurationValidator.Validate(service.Current));
    }

    [Fact]
    public async Task Reload_replaces_current_configuration_after_a_valid_file_change()
    {
        await File.WriteAllTextAsync(ConfigPath, """
            {
              "controller": { "updateRateHz": 144 },
              "input": { "suppressMappedKeys": false, "throttleCutBinding": "Space", "emergencyDisableBinding": "Ctrl+Alt+F12" },
              "throttle": { "primaryBinding": "W", "mode": "Ramp", "riseSeconds": 1, "fallSeconds": 0.5, "initialLevel": 0.1, "maximumLevel": 1, "fixedLevels": {}, "curve": "Linear", "customExponent": 1 },
              "brake": { "primaryBinding": "S", "mode": "Ramp", "riseSeconds": 0.5, "fallSeconds": 0.5, "initialLevel": 0.1, "maximumLevel": 1, "fixedLevels": {}, "curve": "Linear", "customExponent": 1 },
              "ratchet": { "increaseBinding": "PageUp", "decreaseBinding": "PageDown", "resetBinding": "Home", "step": 0.1 },
              "logging": { "minimumLevel": "Information", "retainedFileCountLimit": 7 }
            }
            """);
        using var service = new JsonConfigurationService(ConfigPath);

        var result = await service.ReloadAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(144, service.Current.Controller.UpdateRateHz);
    }

    [Fact]
    public async Task Valid_reload_waits_for_runtime_configuration_application()
    {
        await File.WriteAllTextAsync(ConfigPath, "{ \"controller\": { \"updateRateHz\": 144 } }");
        using var service = new JsonConfigurationService(ConfigPath);
        var applied = false;
        service.ConfigurationChanged += async (configuration, cancellationToken) =>
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            applied = configuration.Controller.UpdateRateHz == 144;
        };

        var result = await service.ReloadAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(applied);
    }

    [Fact]
    public async Task Runtime_application_failure_keeps_the_previous_configuration_unpublished()
    {
        using var service = new JsonConfigurationService(ConfigPath);
        Assert.True((await service.ReloadAsync(CancellationToken.None)).IsSuccess);
        var original = service.Current;
        AppConfiguration? currentSeenBySubscriber = null;
        service.ConfigurationChanged += (_, _) =>
        {
            currentSeenBySubscriber = service.Current;
            throw new InvalidOperationException("Candidate engine could not start.");
        };
        await File.WriteAllTextAsync(ConfigPath, "{ \"controller\": { \"updateRateHz\": 144 } }");

        var result = await service.ReloadAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error =>
            error.PropertyName == "$" &&
            error.Message == "Configuration could not be applied: Candidate engine could not start.");
        Assert.Same(original, currentSeenBySubscriber);
        Assert.Same(original, service.Current);
    }

    [Fact]
    public async Task Invalid_reload_preserves_the_previous_valid_configuration()
    {
        using var service = new JsonConfigurationService(ConfigPath);
        Assert.True((await service.ReloadAsync(CancellationToken.None)).IsSuccess);
        var original = service.Current;
        await File.WriteAllTextAsync(ConfigPath, "{ \"controller\": { \"updateRateHz\": 999 } }");

        var result = await service.ReloadAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error =>
            error.PropertyName == "Controller.UpdateRateHz" &&
            error.Message == "Update rate must be between 30 and 250 Hz.");
        Assert.Same(original, service.Current);
    }

    [Fact]
    public async Task Malformed_json_returns_a_readable_error_without_changing_current_configuration()
    {
        using var service = new JsonConfigurationService(ConfigPath);
        Assert.True((await service.ReloadAsync(CancellationToken.None)).IsSuccess);
        var original = service.Current;
        await File.WriteAllTextAsync(ConfigPath, "{ \"controller\": ");

        var result = await service.ReloadAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.PropertyName == "$" && error.Message.StartsWith("Configuration JSON is invalid:", StringComparison.Ordinal));
        Assert.Same(original, service.Current);
    }

    [Fact]
    public async Task Save_writes_a_configuration_that_can_be_loaded_by_a_new_service()
    {
        var expected = AppConfiguration.CreateDefault() with
        {
            Controller = AppConfiguration.CreateDefault().Controller with { UpdateRateHz = 180 }
        };
        using var writer = new JsonConfigurationService(ConfigPath);

        await writer.SaveAsync(expected, CancellationToken.None);
        using var reader = new JsonConfigurationService(ConfigPath);
        var result = await reader.ReloadAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(180, reader.Current.Controller.UpdateRateHz);
    }

    [Fact]
    public async Task Update_applies_a_transform_after_an_overlapping_reload_releases_the_operation_gate()
    {
        using var service = new JsonConfigurationService(ConfigPath);
        Assert.True((await service.ReloadAsync(CancellationToken.None)).IsSuccess);
        var reloadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowReloadToFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockFirstPublication = true;
        service.ConfigurationChanged += async (_, _) =>
        {
            if (!blockFirstPublication)
            {
                return;
            }

            blockFirstPublication = false;
            reloadEntered.TrySetResult();
            await allowReloadToFinish.Task;
        };
        await File.WriteAllTextAsync(ConfigPath, "{ \"controller\": { \"updateRateHz\": 60 } }");

        var reload = service.ReloadAsync(CancellationToken.None);
        await reloadEntered.Task;
        var update = service.UpdateAsync(
            configuration => configuration with
            {
                Throttle = configuration.Throttle with { PrimaryBinding = "Y" }
            },
            CancellationToken.None);
        allowReloadToFinish.TrySetResult();

        Assert.True((await reload).IsSuccess);
        await update;
        Assert.Equal(60, service.Current.Controller.UpdateRateHz);
        Assert.Equal("Y", service.Current.Throttle.PrimaryBinding);
    }

    [Fact]
    public async Task Deleting_the_configuration_file_requests_a_debounced_reload_that_recreates_the_default()
    {
        using var service = new JsonConfigurationService(ConfigPath);
        Assert.True((await service.ReloadAsync(CancellationToken.None)).IsSuccess);
        File.Delete(ConfigPath);

        await WaitUntilAsync(() => File.Exists(ConfigPath));

        Assert.Empty(ConfigurationValidator.Validate(service.Current));
    }

    [Fact]
    public async Task Watcher_reports_invalid_reload_errors_without_replacing_current_configuration()
    {
        var logger = new RecordingLogger<JsonConfigurationService>();
        using var service = new JsonConfigurationService(ConfigPath, logger);
        Assert.True((await service.ReloadAsync(CancellationToken.None)).IsSuccess);
        var original = service.Current;

        await File.WriteAllTextAsync(ConfigPath, "{ \"controller\": { \"updateRateHz\": 999 } }");

        await WaitUntilAsync(() => logger.Errors.Count != 0);

        Assert.Contains(logger.Errors, message =>
            message.Contains("Configuration watcher reload failed for Controller.UpdateRateHz", StringComparison.Ordinal));
        Assert.Same(original, service.Current);
    }

    [Fact]
    public async Task Undefined_numeric_mode_fails_reload_before_application_services_are_resolved()
    {
        await File.WriteAllTextAsync(ConfigPath, "{ \"throttle\": { \"mode\": 999 } }");
        using var configuration = new JsonConfigurationService(ConfigPath);
        var services = new RejectingServiceProvider();
        await using var lifetime = new ApplicationLifetimeService(
            configuration,
            services,
            NullLogger<ApplicationLifetimeService>.Instance);

        var result = await lifetime.InitializeAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.PropertyName == "Throttle.Mode" && error.Message == "Throttle mode is invalid.");
        Assert.Equal(0, services.RequestCount);
    }

    [Fact]
    public async Task Completed_emergency_stop_does_not_suppress_a_later_emergency_stop()
    {
        using var configuration = new JsonConfigurationService(ConfigPath);
        var session = new CountingSession();
        var services = new FixedServiceProvider(session);
        await using var lifetime = new ApplicationLifetimeService(
            configuration,
            services,
            NullLogger<ApplicationLifetimeService>.Instance);
        Assert.True((await lifetime.InitializeAsync(CancellationToken.None)).IsSuccess);

        lifetime.RequestEmergencyStop();
        await WaitUntilAsync(() => session.EmergencyResetCount == 1);
        lifetime.RequestEmergencyStop();
        await WaitUntilAsync(() => session.EmergencyResetCount == 2);
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected file-system change was not observed.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class RejectingServiceProvider : IServiceProvider
    {
        public int RequestCount { get; private set; }

        public object? GetService(Type serviceType)
        {
            RequestCount++;
            throw new InvalidOperationException($"Application services must not resolve when configuration is invalid: {serviceType.Name}.");
        }
    }

    private sealed class FixedServiceProvider(IEmulationSession session) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IEmulationSession) ? session : null;
    }

    private sealed class CountingSession : IEmulationSession
    {
        public int EmergencyResetCount { get; private set; }

        public EmulationState State => EmulationState.Stopped;

        public event EventHandler<EmulationState>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ControllerTestProgress>? ControllerTestProgressChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task EmergencyResetAsync(CancellationToken cancellationToken)
        {
            EmergencyResetCount++;
            return Task.CompletedTask;
        }

        public Task ReconfigureAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RunControllerTestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}
