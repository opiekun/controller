using KeyboardAnalogThrottle.Core.Output;

namespace KeyboardAnalogThrottle.Core.Tests.Output;

public sealed class TriggerConverterTests
{
    [Theory]
    [InlineData(0.25, (byte)64)]
    [InlineData(0.50, (byte)128)]
    [InlineData(0.75, (byte)191)]
    public void Converts_normalized_trigger_values_with_away_from_zero_rounding(double input, byte expected) =>
        Assert.Equal(expected, TriggerConverter.ToByte(input));

    [Theory]
    [InlineData(-.1, (byte)0)]
    [InlineData(1.1, byte.MaxValue)]
    [InlineData(0, (byte)0)]
    [InlineData(1, byte.MaxValue)]
    public void Clamps_normalized_values_before_conversion(double input, byte expected) =>
        Assert.Equal(expected, TriggerConverter.ToByte(input));
}
