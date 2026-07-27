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
        FixedLevels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Shift+W"] = .5d,
            ["Ctrl+W"] = 1d
        }
    };

    public static ChannelConfiguration CreateBrakeDefault() => new()
    {
        PrimaryBinding = "S",
        RiseSeconds = .5d,
        FixedLevels = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Shift+S"] = .5d,
            ["Ctrl+S"] = 1d
        }
    };
}

public enum InputMode
{
    Ramp,
    Fixed,
    Ratchet
}
