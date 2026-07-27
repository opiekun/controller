using KeyboardAnalogThrottle.Core.Abstractions;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Controller;

/// <summary>
/// Creates a controller without leaking the ViGEm client or target types to consumers.
/// </summary>
public sealed class VigemControllerFactory
{
    public IVirtualController Create() => new VigemXbox360Controller();
}
