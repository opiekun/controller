namespace KeyboardAnalogThrottle.Core.Input;

/// <summary>
/// An immutable view of pressed keys and the distinct transitions that occurred in a frame.
/// </summary>
public sealed class InputSnapshot
{
    private readonly HashSet<InputKey> _pressedKeys;
    private readonly HashSet<InputKey> _pressedThisFrame;
    private readonly Dictionary<InputKey, long> _transitionSequences;

    public static InputSnapshot Empty { get; } = new([], []);

    public InputSnapshot(IEnumerable<InputKey> pressedKeys, IEnumerable<KeyTransition> transitions)
    {
        ArgumentNullException.ThrowIfNull(pressedKeys);
        ArgumentNullException.ThrowIfNull(transitions);

        _pressedKeys = new HashSet<InputKey>(pressedKeys);
        _pressedThisFrame = new HashSet<InputKey>();
        _transitionSequences = new Dictionary<InputKey, long>();

        foreach (var transition in transitions)
        {
            _transitionSequences[transition.Key] = transition.Sequence;
            if (transition.IsDown)
            {
                _pressedThisFrame.Add(transition.Key);
            }
        }
    }

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

    public static InputSnapshot FromPressed(params InputKey[] pressedKeys)
    {
        ArgumentNullException.ThrowIfNull(pressedKeys);
        var transitions = new KeyTransition[pressedKeys.Length];
        for (var index = 0; index < pressedKeys.Length; index++)
        {
            transitions[index] = new KeyTransition(pressedKeys[index], true, index + 1L);
        }

        return new InputSnapshot(pressedKeys, transitions);
    }

    public bool IsPressed(InputKey key) => _pressedKeys.Contains(key);

    public bool WasPressedThisFrame(InputKey key) => _pressedThisFrame.Contains(key);

    public long TransitionSequence(InputKey key) =>
        _transitionSequences.TryGetValue(key, out var sequence) ? sequence : 0;
}
