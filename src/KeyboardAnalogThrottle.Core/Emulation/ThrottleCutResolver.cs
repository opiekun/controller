using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Emulation;

/// <summary>
/// Applies hold or transition-driven toggle throttle cuts.
/// </summary>
public sealed class ThrottleCutResolver
{
    private bool _toggleCutActive;

    public double Resolve(InputSnapshot input, InputBinding cutBinding, double throttle, bool toggle)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!toggle)
        {
            return cutBinding.Matches(input) ? 0d : throttle;
        }

        if (input.WasPressedThisFrame(cutBinding.Primary) &&
            (input.TransitionModifiers(cutBinding.Primary) & cutBinding.Modifiers) == cutBinding.Modifiers)
        {
            _toggleCutActive = !_toggleCutActive;
        }

        return _toggleCutActive ? 0d : throttle;
    }

    public void Reset() => _toggleCutActive = false;
}
