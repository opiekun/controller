using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Input;
using System.Runtime.CompilerServices;

namespace KeyboardAnalogThrottle.Core.Strategies;

/// <summary>
/// Selects a configured fixed level without retaining or ramping prior output.
/// </summary>
public sealed class FixedInputStrategy : IInputStrategy
{
    private static readonly ConditionalWeakTable<ChannelConfiguration, PreparedConfiguration> PreparedConfigurations = new();

    public double Update(InputSnapshot input, double currentValue, TimeSpan elapsed, ChannelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);

        var prepared = PreparedConfigurations.GetValue(configuration, static channel => new PreparedConfiguration(channel));
        if (!prepared.PrimaryBinding.Matches(input))
        {
            return 0d;
        }

        var selected = FixedBindingResolver.Resolve(input, prepared.Levels);
        var level = selected ?? configuration.MaximumLevel;
        var maximum = Normalize(configuration.MaximumLevel);
        return Math.Min(maximum, Normalize(level));
    }

    private static double Normalize(double value) => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;

    private sealed class PreparedConfiguration
    {
        public PreparedConfiguration(ChannelConfiguration configuration)
        {
            PrimaryBinding = BindingParser.Parse(configuration.PrimaryBinding);
            Levels = configuration.FixedLevels.ToDictionary(
                static entry => BindingParser.Parse(entry.Key),
                static entry => entry.Value);
        }

        public InputBinding PrimaryBinding { get; }

        public IReadOnlyDictionary<InputBinding, double> Levels { get; }
    }
}
