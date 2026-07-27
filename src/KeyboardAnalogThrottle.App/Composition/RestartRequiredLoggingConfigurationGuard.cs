using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;

namespace KeyboardAnalogThrottle.App.Composition;

/// <summary>
/// Serilog's file-sink retention cannot be changed after the provider is built, so logging changes require a restart.
/// </summary>
public sealed class RestartRequiredLoggingConfigurationGuard : IDisposable
{
    private readonly IConfigurationService _configuration;
    private readonly LoggingConfiguration _applied;
    private int _disposed;

    public RestartRequiredLoggingConfigurationGuard(IConfigurationService configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _applied = configuration.Current.Logging;
        _configuration.ConfigurationChanged += OnConfigurationChangedAsync;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _configuration.ConfigurationChanged -= OnConfigurationChangedAsync;
        }
    }

    private Task OnConfigurationChangedAsync(AppConfiguration candidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_applied.Equals(candidate.Logging))
        {
            throw new InvalidOperationException("Logging configuration changes require application restart.");
        }

        return Task.CompletedTask;
    }
}
