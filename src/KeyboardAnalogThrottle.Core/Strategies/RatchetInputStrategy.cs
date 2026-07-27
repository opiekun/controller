using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Strategies;

/// <summary>
/// Applies ratchet changes only for distinct physical key-down transitions.
/// </summary>
public sealed class RatchetInputStrategy : IInputStrategy
{
    private readonly InputBinding _increaseBinding;
    private readonly InputBinding _decreaseBinding;
    private readonly InputBinding _resetBinding;
    private readonly double _step;

    public RatchetInputStrategy(RatchetConfiguration? configuration = null)
    {
        var ratchet = configuration ?? RatchetConfiguration.Default;
        _increaseBinding = BindingParser.Parse(ratchet.IncreaseBinding);
        _decreaseBinding = BindingParser.Parse(ratchet.DecreaseBinding);
        _resetBinding = BindingParser.Parse(ratchet.ResetBinding);
        _step = Normalize(ratchet.Step);
    }

    public double Update(InputSnapshot input, double currentValue, TimeSpan elapsed, ChannelConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(configuration);

        var maximum = Normalize(configuration.MaximumLevel);
        var value = Math.Clamp(Normalize(currentValue), 0d, maximum);

        var transitions = input.Transitions;
        for (var index = 0; index < transitions.Count; index++)
        {
            var transition = transitions[index];
            if (_increaseBinding.Matches(transition))
            {
                value = Math.Min(maximum, value + _step);
            }

            if (_decreaseBinding.Matches(transition))
            {
                value = Math.Max(0d, value - _step);
            }

            if (_resetBinding.Matches(transition))
            {
                value = 0d;
            }
        }

        return value;
    }

    private static double Normalize(double value) => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;
}
