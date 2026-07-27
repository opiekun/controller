using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Emulation;

/// <summary>
/// Resolves throttle and brake output when simultaneous base-key input is disabled.
/// </summary>
public static class ConflictResolver
{
    public static (double Throttle, double Brake) Resolve(
        InputSnapshot input,
        double throttle,
        double brake,
        InputKey throttleKey,
        InputKey brakeKey,
        ConflictMode mode,
        bool simultaneousInputEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (simultaneousInputEnabled || !input.IsPressed(throttleKey) || !input.IsPressed(brakeKey))
        {
            return (throttle, brake);
        }

        return mode switch
        {
            ConflictMode.BrakeWins => (0d, brake),
            ConflictMode.ThrottleWins => (throttle, 0d),
            ConflictMode.CancelBoth => (0d, 0d),
            ConflictMode.LastPressedWins => ResolveLastPressed(input, throttle, brake, throttleKey, brakeKey),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported conflict mode.")
        };
    }

    private static (double Throttle, double Brake) ResolveLastPressed(
        InputSnapshot input,
        double throttle,
        double brake,
        InputKey throttleKey,
        InputKey brakeKey)
    {
        var throttleSequence = input.TransitionSequence(throttleKey);
        var brakeSequence = input.TransitionSequence(brakeKey);

        return throttleSequence > brakeSequence ? (throttle, 0d)
            : brakeSequence > throttleSequence ? (0d, brake)
            : (0d, 0d);
    }
}
