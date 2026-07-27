namespace KeyboardAnalogThrottle.Core.Configuration;

/// <summary>
/// A serializable fixed level entry for callers that prefer a list over the legacy binding map.
/// </summary>
public sealed record FixedLevelConfiguration
{
    public string Binding { get; init; } = string.Empty;

    public double Level { get; init; }
}
