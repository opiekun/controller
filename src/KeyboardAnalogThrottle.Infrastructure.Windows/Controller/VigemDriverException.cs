namespace KeyboardAnalogThrottle.Infrastructure.Windows.Controller;

/// <summary>
/// Indicates that the separately installed ViGEmBus driver is unavailable to the application.
/// </summary>
public sealed class VigemDriverException : Exception
{
    public const string InstallationMessage =
        "ViGEmBus is not installed or unavailable. Install ViGEmBus manually from https://github.com/nefarius/ViGEmBus/releases, then restart Keyboard Analog Throttle. The application never downloads or installs drivers.";

    public VigemDriverException(Exception innerException)
        : base(InstallationMessage, innerException)
    {
    }
}
