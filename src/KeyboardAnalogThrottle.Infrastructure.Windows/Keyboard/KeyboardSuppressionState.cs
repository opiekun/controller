using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

/// <summary>
/// Immutable, lock-free key state used by the low-level hook's suppression decision.
/// </summary>
public readonly record struct KeyboardSuppressionState(ulong PressedKeyMask)
{
    public InputModifiers Modifiers
    {
        get
        {
            var modifiers = InputModifiers.None;
            if (IsPressed(InputKey.LeftControl) || IsPressed(InputKey.RightControl)) modifiers |= InputModifiers.Control;
            if (IsPressed(InputKey.LeftAlt) || IsPressed(InputKey.RightAlt)) modifiers |= InputModifiers.Alt;
            if (IsPressed(InputKey.LeftShift) || IsPressed(InputKey.RightShift)) modifiers |= InputModifiers.Shift;
            return modifiers;
        }
    }

    public bool IsPressed(InputKey key) => (PressedKeyMask & KeyMask(key)) != 0;

    public static ulong KeyMask(InputKey key) => key is not InputKey.None && (uint)key < 64 ? 1UL << (int)key : 0UL;
}
