using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

/// <summary>
/// Describes one distinct physical key transition accepted by the input state store.
/// </summary>
public sealed class KeyStateChangedEventArgs(KeyTransition transition) : EventArgs
{
    public KeyTransition Transition { get; } = transition;
}
