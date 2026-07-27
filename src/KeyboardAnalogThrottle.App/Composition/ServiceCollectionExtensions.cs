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

        var logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyboardAnalogThrottle",
            "Logs");
        Directory.CreateDirectory(logsDirectory);
        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logsDirectory, "keyboard-analog-throttle-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        services.AddLogging(builder => builder.AddSerilog(logger, dispose: true));
        services.AddSingleton<JsonConfigurationService>();
        services.AddSingleton<IConfigurationService>(provider => provider.GetRequiredService<JsonConfigurationService>());
        services.AddSingleton<VigemControllerFactory>();
        services.AddSingleton<IVirtualController>(provider => provider.GetRequiredService<VigemControllerFactory>().Create());
        services.AddSingleton<LowLevelKeyboardInputSource>(provider =>
            new LowLevelKeyboardInputSource(provider.GetRequiredService<IConfigurationService>().Current));
        services.AddSingleton<IKeyboardInputSource>(provider => provider.GetRequiredService<LowLevelKeyboardInputSource>());
        services.AddSingleton<IClock, StopwatchClock>();
        services.AddSingleton<IEmulationEngine>(provider => new EmulationEngine(
            provider.GetRequiredService<IConfigurationService>().Current,
            provider.GetRequiredService<IVirtualController>(),
            provider.GetRequiredService<IKeyboardInputSource>(),
            provider.GetRequiredService<IClock>()));
        services.AddSingleton<IControllerTestService, ControllerTestService>();
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
