namespace KeyboardAnalogThrottle.Core.Configuration;

/// <summary>
/// Serializable settings for an individual throttle or brake channel.
/// </summary>
public sealed record ChannelConfiguration
{
    public string PrimaryBinding { get; init; } = "W";

    public InputMode Mode { get; init; } = InputMode.Ramp;

    public double RiseSeconds { get; init; } = 1d;

    public double FallSeconds { get; init; } = .5d;

    public double InitialLevel { get; init; } = .1d;

    public double MaximumLevel { get; init; } = 1d;

    public IReadOnlyDictionary<string, double> FixedLevels { get; init; } = new Dictionary<string, double>();

    public string Curve { get; init; } = "Linear";

    public double CustomExponent { get; init; } = 1d;

    public static ChannelConfiguration DefaultThrottle => CreateThrottleDefault();

    public static ChannelConfiguration DefaultBrake => CreateBrakeDefault();

    public static ChannelConfiguration CreateThrottleDefault() => new()
    {
        PrimaryBinding = "W",
        RiseSeconds = 1.2d,
        FallSeconds = .45d,
        InitialLevel = .08d,
        MaximumLevel = 1d,
        Curve = "EaseOut",
        FixedLevels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl+W"] = .25d,
            ["Alt+W"] = .5d,
            ["Shift+W"] = .75d,
            ["Ctrl+Shift+W"] = 1d
        }
    };

    public static ChannelConfiguration CreateBrakeDefault() => new()
    {
        PrimaryBinding = "S",
        RiseSeconds = .3d,
        FallSeconds = .2d,
        InitialLevel = 0d,
        MaximumLevel = 1d,
        Curve = "Linear",
        FixedLevels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl+S"] = .25d,
            ["Alt+S"] = .5d,
            ["Shift+S"] = .75d,
            ["Ctrl+Shift+S"] = 1d
        }
    };
}

public enum InputMode
{
    Ramp,
    Fixed,
    Ratchet
}
