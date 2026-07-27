using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Strategies;

/// <summary>
/// Linearly rises while the primary binding is active and falls when it is released.
/// </summary>
public sealed class RampInputStrategy : IInputStrategy
{
    public double Update(InputSnapshot input, double currentValue, TimeSpan elapsed, ChannelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);

        var binding = BindingParser.Parse(configuration.PrimaryBinding);
        var maximum = NormalizedMaximum(configuration.MaximumLevel);
        var value = Math.Clamp(FiniteOrZero(currentValue), 0d, maximum);

        if (binding.Matches(input))
        {
            if (input.WasPressedThisFrame(binding.Primary))
            {
                value = Math.Max(value, Math.Min(NormalizedMaximum(configuration.InitialLevel), maximum));
            }

            return Math.Min(maximum, value + Rate(maximum, configuration.RiseSeconds) * PositiveSeconds(elapsed));
        }

        return Math.Max(0d, value - Rate(maximum, configuration.FallSeconds) * PositiveSeconds(elapsed));
    }

    private static double Rate(double maximum, double seconds) =>
        double.IsFinite(seconds) && seconds > 0d ? maximum / seconds : 0d;

    private static double PositiveSeconds(TimeSpan elapsed) => Math.Max(0d, elapsed.TotalSeconds);

    private static double NormalizedMaximum(double value) => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0d;
}
