using System.Diagnostics;
using System.IO;
using KeyboardAnalogThrottle.App.Services;
using KeyboardAnalogThrottle.App.ViewModels;
using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Infrastructure.Windows.Controller;
using KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;
using KeyboardAnalogThrottle.Infrastructure.Windows.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KeyboardAnalogThrottle.App.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKeyboardAnalogThrottle(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var configurationService = new JsonConfigurationService();
        _ = configurationService.ReloadAsync(CancellationToken.None).GetAwaiter().GetResult();

        var logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyboardAnalogThrottle",
            "Logs");
        var logger = LoggingConfigurationFactory.Create(configurationService.Current.Logging, logsDirectory);

        services.AddLogging(builder => builder.AddSerilog(logger, dispose: true));
        services.AddSingleton(configurationService);
        services.AddSingleton<IConfigurationService>(configurationService);
        services.AddSingleton(new RestartRequiredLoggingConfigurationGuard(configurationService));
        services.AddSingleton<VigemControllerFactory>();
        services.AddSingleton<IClock, StopwatchClock>();
        services.AddSingleton<IControllerTestService, ControllerTestService>();
        services.AddSingleton<IEmulationSession>(provider =>
        {
            LowLevelKeyboardInputSource? input = null;
            return new EmulationSession(
                configuration =>
                {
                    input = new LowLevelKeyboardInputSource(
                        configuration,
                        provider.GetRequiredService<ILogger<LowLevelKeyboardInputSource>>());
                    return new EmulationEngine(
                        configuration,
                        provider.GetRequiredService<VigemControllerFactory>().Create(),
                        input,
                        provider.GetRequiredService<IClock>(),
                        logger: provider.GetRequiredService<ILogger<EmulationEngine>>());
                },
                provider.GetRequiredService<IControllerTestService>(),
                isRunning => input?.SetEngineRunning(isRunning),
                provider.GetRequiredService<IConfigurationService>());
        });
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<ApplicationLifetimeService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
        return services;
    }

    private sealed class StopwatchClock : IClock
    {
        private readonly long _startTimestamp = Stopwatch.GetTimestamp();

        public TimeSpan GetTimestamp() => Stopwatch.GetElapsedTime(_startTimestamp);

        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            new(Task.Delay(delay, cancellationToken));
    }
}
