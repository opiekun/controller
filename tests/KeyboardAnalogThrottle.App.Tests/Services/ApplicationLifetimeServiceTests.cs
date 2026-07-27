using KeyboardAnalogThrottle.App.Composition;
using KeyboardAnalogThrottle.App.Services;
using KeyboardAnalogThrottle.App.ViewModels;
using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyboardAnalogThrottle.App.Tests.Services;

public sealed class ApplicationLifetimeServiceTests
{
    [Fact]
    public async Task Initialize_does_not_resolve_the_emulation_engine()
    {
        await using var lifetime = new ApplicationLifetimeService(
            new ValidConfigurationService(),
            new ThrowingEngineProvider(),
            NullLogger<ApplicationLifetimeService>.Instance);

        var result = await lifetime.InitializeAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Dashboard_resolution_does_not_create_emulation_or_virtual_controller()
    {
        var engineResolutions = 0;
        var controllerResolutions = 0;
        var services = new ServiceCollection();
        services.AddKeyboardAnalogThrottle();
        services.AddSingleton<IConfigurationService>(new ValidConfigurationService());
        services.AddSingleton<IEmulationEngine>(_ =>
        {
            engineResolutions++;
            throw new InvalidOperationException("Dashboard resolution created the emulation engine.");
        });
        services.AddSingleton<IVirtualController>(_ =>
        {
            controllerResolutions++;
            throw new InvalidOperationException("Dashboard resolution created the ViGEm controller.");
        });
        await using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<MainWindowViewModel>();

        Assert.Equal(0, engineResolutions);
        Assert.Equal(0, controllerResolutions);
    }

    private sealed class ValidConfigurationService : IConfigurationService
    {
        public AppConfiguration Current { get; } = AppConfiguration.CreateDefault();

        public Task<ConfigurationReloadResult> ReloadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConfigurationReloadResult.Success);

        public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThrowingEngineProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IEmulationEngine))
            {
                throw new InvalidOperationException("The engine must not be constructed during startup.");
            }

            return null;
        }
    }
}
