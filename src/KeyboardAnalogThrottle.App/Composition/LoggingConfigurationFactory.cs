using KeyboardAnalogThrottle.Core.Configuration;
using Serilog;
using Serilog.Events;
using System.IO;

namespace KeyboardAnalogThrottle.App.Composition;

internal static class LoggingConfigurationFactory
{
    public static LoggingSettings Resolve(LoggingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(configuration.MinimumLevel, true, out var level) ||
            !Enum.IsDefined(level))
        {
            throw new ArgumentException("Logging minimum level is invalid.", nameof(configuration));
        }

        return new LoggingSettings(ToSerilogLevel(level), configuration.RetainedFileCountLimit);
    }

    public static ILogger Create(LoggingConfiguration configuration, string logsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        var settings = Resolve(configuration);
        Directory.CreateDirectory(logsDirectory);
        return new LoggerConfiguration()
            .MinimumLevel.Is(settings.MinimumLevel)
            .WriteTo.File(
                Path.Combine(logsDirectory, "keyboard-analog-throttle-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: settings.RetainedFileCountLimit)
            .CreateLogger();
    }

    private static LogEventLevel ToSerilogLevel(Microsoft.Extensions.Logging.LogLevel level) => level switch
    {
        Microsoft.Extensions.Logging.LogLevel.Trace => LogEventLevel.Verbose,
        Microsoft.Extensions.Logging.LogLevel.Debug => LogEventLevel.Debug,
        Microsoft.Extensions.Logging.LogLevel.Information => LogEventLevel.Information,
        Microsoft.Extensions.Logging.LogLevel.Warning => LogEventLevel.Warning,
        Microsoft.Extensions.Logging.LogLevel.Error => LogEventLevel.Error,
        Microsoft.Extensions.Logging.LogLevel.Critical => LogEventLevel.Fatal,
        Microsoft.Extensions.Logging.LogLevel.None => (LogEventLevel)6,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unsupported logging level.")
    };
}

internal sealed record LoggingSettings(LogEventLevel MinimumLevel, int RetainedFileCountLimit);
