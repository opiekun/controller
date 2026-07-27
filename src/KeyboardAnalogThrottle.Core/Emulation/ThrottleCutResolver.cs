using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Emulation;

/// <summary>
/// Applies hold or transition-driven toggle throttle cuts.
/// </summary>
public sealed class ThrottleCutResolver
{
    private bool _toggleCutActive;

    public bool IsActive { get; private set; }

    public double Resolve(InputSnapshot input, InputBinding cutBinding, double throttle, bool toggle)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!toggle)
        {
            IsActive = cutBinding.Matches(input);
            return IsActive ? 0d : throttle;
        }

        var transitions = input.Transitions;
        for (var index = 0; index < transitions.Count; index++)
        {
            var transition = transitions[index];
            if (cutBinding.Matches(transition))
            {
                _toggleCutActive = !_toggleCutActive;
            }
        }

        IsActive = _toggleCutActive;
        return IsActive ? 0d : throttle;
    }

    public void Reset()
    {
        _toggleCutActive = false;
        IsActive = false;
    }
}
