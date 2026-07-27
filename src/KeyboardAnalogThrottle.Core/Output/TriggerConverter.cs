namespace KeyboardAnalogThrottle.Core.Output;

/// <summary>
/// Converts a normalized trigger value to the Xbox trigger byte range.
/// </summary>
public static class TriggerConverter
{
    public static byte ToByte(double value)
    {
        var normalized = !double.IsFinite(value) ? 0d : Math.Clamp(value, 0d, 1d);
        return (byte)Math.Round(normalized * byte.MaxValue, MidpointRounding.AwayFromZero);
    }
}
