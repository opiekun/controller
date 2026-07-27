using KeyboardAnalogThrottle.Core.Configuration;

namespace KeyboardAnalogThrottle.Core.Abstractions;

public interface IConfigurationService
{
    AppConfiguration Current { get; }

    /// <summary>
    /// Invoked for a validated configuration before a reload completes. Subscribers use this
    /// to atomically rebuild runtime resources from the new snapshot.
    /// </summary>
    event Func<AppConfiguration, CancellationToken, Task>? ConfigurationChanged;

    Task<ConfigurationReloadResult> ReloadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken);
}

public sealed record ConfigurationReloadResult(
    bool IsSuccess,
    IReadOnlyList<ConfigurationValidationError> Errors)
{
    public static ConfigurationReloadResult Success { get; } = new(true, Array.Empty<ConfigurationValidationError>());

    public static ConfigurationReloadResult Failure(IReadOnlyList<ConfigurationValidationError> errors) => new(false, errors);
}
