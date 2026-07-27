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
        var events = new (InputBinding Binding, Action Apply, long Sequence)[]
        {
            (_increaseBinding, () => value = Math.Min(maximum, value + _step), input.TransitionSequence(_increaseBinding.Primary)),
            (_decreaseBinding, () => value = Math.Max(0d, value - _step), input.TransitionSequence(_decreaseBinding.Primary)),
            (_resetBinding, () => value = 0d, input.TransitionSequence(_resetBinding.Primary))
        };

        foreach (var transition in events.Where(candidate => IsDownTransition(input, candidate.Binding)).OrderBy(candidate => candidate.Sequence))
        {
            transition.Apply();
        }

        return value;
    }

    private static bool IsDownTransition(InputSnapshot input, InputBinding binding) =>
        input.WasPressedThisFrame(binding.Primary) &&
        (input.TransitionModifiers(binding.Primary) & binding.Modifiers) == binding.Modifiers;

    private static double Normalize(double value) => double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;
}
