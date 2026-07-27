using KeyboardAnalogThrottle.Core.Curves;

namespace KeyboardAnalogThrottle.Core.Tests.Curves;

public sealed class OutputCurveTests
{
    [Theory]
    [InlineData(CurveKind.Linear, .5, 1, .5)]
    [InlineData(CurveKind.EaseIn, .5, 1, .25)]
    [InlineData(CurveKind.EaseOut, .5, 1, .75)]
    [InlineData(CurveKind.SmoothStep, .5, 1, .5)]
    [InlineData(CurveKind.Exponent, .5, 3, .125)]
    public void Applies_the_selected_curve(CurveKind kind, double input, double exponent, double expected) =>
        Assert.Equal(expected, OutputCurve.Apply(input, kind, exponent), 10);

    [Theory]
    [InlineData(CurveKind.Linear)]
    [InlineData(CurveKind.EaseIn)]
    [InlineData(CurveKind.EaseOut)]
    [InlineData(CurveKind.SmoothStep)]
    [InlineData(CurveKind.Exponent)]
    public void Clamps_before_and_after_the_curve(CurveKind kind)
    {
        Assert.Equal(0, OutputCurve.Apply(-.25, kind, 2));
        Assert.Equal(1, OutputCurve.Apply(1.25, kind, 2));
    }

    [Theory]
    [InlineData(CurveKind.Linear)]
    [InlineData(CurveKind.EaseIn)]
    [InlineData(CurveKind.EaseOut)]
    [InlineData(CurveKind.SmoothStep)]
    [InlineData(CurveKind.Exponent)]
    public void Preserves_curve_endpoints(CurveKind kind)
    {
        Assert.Equal(0, OutputCurve.Apply(0, kind, 2));
        Assert.Equal(1, OutputCurve.Apply(1, kind, 2));
    }
}
