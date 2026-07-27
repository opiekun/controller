using KeyboardAnalogThrottle.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Controller;

/// <summary>
/// Creates a controller without leaking the ViGEm client or target types to consumers.
/// </summary>
public sealed class VigemControllerFactory
{
    private readonly ILogger<VigemXbox360Controller> _logger;

    public VigemControllerFactory(ILogger<VigemXbox360Controller> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IVirtualController Create() => new VigemXbox360Controller(_logger);
}
