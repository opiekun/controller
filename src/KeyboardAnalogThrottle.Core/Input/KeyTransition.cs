namespace KeyboardAnalogThrottle.Core.Input;

/// <summary>
/// A distinct physical key state change observed by the input source.
/// </summary>
public readonly record struct KeyTransition(
    InputKey Key,
    bool IsDown,
    long Sequence,
    InputModifiers Modifiers = InputModifiers.None)
{
    public bool IsPressed => IsDown;
}
