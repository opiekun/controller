using KeyboardAnalogThrottle.Core.Configuration;

namespace KeyboardAnalogThrottle.Core.Abstractions;

public interface IConfigurationService
{
    AppConfiguration Current { get; }

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
