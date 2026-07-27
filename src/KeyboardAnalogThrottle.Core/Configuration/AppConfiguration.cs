namespace KeyboardAnalogThrottle.Core.Configuration;

using KeyboardAnalogThrottle.Core.Emulation;

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

    /// <summary>Whether both trigger outputs may be active at the same time.</summary>
    public bool AllowSimultaneousThrottleAndBrake { get; init; } = true;

    /// <summary>The output rule used when simultaneous trigger output is disabled.</summary>
    public ConflictMode ConflictMode { get; init; } = ConflictMode.BrakeWins;
}

public sealed record InputConfiguration
{
    public static InputConfiguration Default { get; } = new();

    public bool SuppressMappedKeys { get; init; } = true;

    public string ThrottleCutBinding { get; init; } = "Space";

    public ThrottleCutMode ThrottleCutMode { get; init; } = ThrottleCutMode.Hold;

    public string EmergencyDisableBinding { get; init; } = "Ctrl+Alt+F12";
}

public sealed record RatchetConfiguration
{
    public static RatchetConfiguration Default { get; } = new();

    public string IncreaseBinding { get; init; } = "W";

    public string DecreaseBinding { get; init; } = "Q";

    public string ResetBinding { get; init; } = "Space";

    public double Step { get; init; } = .1d;
}

public enum ThrottleCutMode
{
    Hold,
    Toggle
}

public sealed record LoggingConfiguration
{
    public static LoggingConfiguration Default { get; } = new();

    public string MinimumLevel { get; init; } = "Information";

    public int RetainedFileCountLimit { get; init; } = 7;
}
