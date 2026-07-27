using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Input;
using System.Runtime.CompilerServices;

namespace KeyboardAnalogThrottle.Core.Strategies;

/// <summary>
/// Linearly rises while the primary binding is active and falls when it is released.
/// </summary>
public sealed class RampInputStrategy : IInputStrategy
{
    private static readonly ConditionalWeakTable<ChannelConfiguration, PreparedConfiguration> PreparedConfigurations = new();

    public double Update(InputSnapshot input, double currentValue, TimeSpan elapsed, ChannelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);

        var binding = PreparedConfigurations.GetValue(configuration, static channel => new PreparedConfiguration(channel)).PrimaryBinding;
        var maximum = NormalizedMaximum(configuration.MaximumLevel);
        var value = Math.Clamp(FiniteOrZero(currentValue), 0d, maximum);

        var transitions = input.Transitions;
        for (var index = 0; index < transitions.Count; index++)
        {
            var transition = transitions[index];
            if (binding.Matches(transition))
            {
                value = Math.Max(value, Math.Min(NormalizedMaximum(configuration.InitialLevel), maximum));
                break;
            }
        }

        if (binding.Matches(input))
        {
            return Math.Min(maximum, value + Rate(maximum, configuration.RiseSeconds) * PositiveSeconds(elapsed));
        }

        return Math.Max(0d, value - Rate(maximum, configuration.FallSeconds) * PositiveSeconds(elapsed));
    }

    private static double Rate(double maximum, double seconds) =>
        double.IsFinite(seconds) && seconds > 0d ? maximum / seconds : 0d;

    private static double PositiveSeconds(TimeSpan elapsed) => Math.Max(0d, elapsed.TotalSeconds);

    private static double NormalizedMaximum(double value) => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0d;

    private sealed class PreparedConfiguration
    {
        public PreparedConfiguration(ChannelConfiguration configuration)
        {
            PrimaryBinding = BindingParser.Parse(configuration.PrimaryBinding);
        }

        public InputBinding PrimaryBinding { get; }
    }
}
