namespace KeyboardAnalogThrottle.Core.Curves;

/// <summary>
/// Stateless normalized output-curve functions.
/// </summary>
public static class OutputCurve
{
    public static double Apply(double value, CurveKind kind, double exponent)
    {
        var input = Clamp(value);
        var output = kind switch
        {
            CurveKind.Linear => input,
            CurveKind.EaseIn => input * input,
            CurveKind.EaseOut => 1d - ((1d - input) * (1d - input)),
            CurveKind.SmoothStep => input * input * (3d - (2d * input)),
            CurveKind.Exponent => Math.Pow(input, exponent),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported curve kind.")
        };

        return Clamp(output);
    }

    private static double Clamp(double value) => !double.IsFinite(value) ? 0d : Math.Clamp(value, 0d, 1d);
}
