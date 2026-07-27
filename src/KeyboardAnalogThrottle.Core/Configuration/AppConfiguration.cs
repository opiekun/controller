namespace KeyboardAnalogThrottle.Core.Configuration;

/// <summary>
/// The complete, serializable application configuration.
/// </summary>
public sealed record AppConfiguration
{
    public ControllerConfiguration Controller { get; init; } = ControllerConfiguration.Default;

    public InputConfiguration Input { get; init; } = InputConfiguration.Default;

    public ChannelConfiguration Throttle { get; init; } = ChannelConfiguration.CreateThrottleDefault();

    public ChannelConfiguration Brake { get; init; } = ChannelConfiguration.CreateBrakeDefault();

    public RatchetConfiguration Ratchet { get; init; } = RatchetConfiguration.Default;

    public LoggingConfiguration Logging { get; init; } = LoggingConfiguration.Default;

    public static AppConfiguration CreateDefault() => new();
}

public sealed record ControllerConfiguration
{
    public static ControllerConfiguration Default { get; } = new();

    public int UpdateRateHz { get; init; } = 120;

    public int MaximumFrameDeltaMilliseconds { get; init; } = 50;

    public int InputLossTimeoutMilliseconds { get; init; } = 1_000;
}

public sealed record InputConfiguration
{
    public static InputConfiguration Default { get; } = new();

    public bool SuppressMappedKeys { get; init; }

    public string ThrottleCutBinding { get; init; } = "Space";

    public string EmergencyDisableBinding { get; init; } = "Ctrl+Alt+F12";
}

public sealed record RatchetConfiguration
{
    public static RatchetConfiguration Default { get; } = new();

    public string IncreaseBinding { get; init; } = "PageUp";

    public string DecreaseBinding { get; init; } = "PageDown";

    public string ResetBinding { get; init; } = "Home";

    public double Step { get; init; } = .1d;
}

public sealed record LoggingConfiguration
{
    public static LoggingConfiguration Default { get; } = new();

    public string MinimumLevel { get; init; } = "Information";

    public int RetainedFileCountLimit { get; init; } = 7;
}
