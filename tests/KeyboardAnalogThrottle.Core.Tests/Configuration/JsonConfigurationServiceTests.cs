using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Infrastructure.Windows.Lifecycle;

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
    public async Task Deleting_the_configuration_file_requests_a_debounced_reload_that_recreates_the_default()
    {
        using var service = new JsonConfigurationService(ConfigPath);
        Assert.True((await service.ReloadAsync(CancellationToken.None)).IsSuccess);
        File.Delete(ConfigPath);

        await WaitUntilAsync(() => File.Exists(ConfigPath));

        Assert.Empty(ConfigurationValidator.Validate(service.Current));
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
}
