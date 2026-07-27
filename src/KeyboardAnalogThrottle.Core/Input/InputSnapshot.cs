namespace KeyboardAnalogThrottle.Core.Input;

/// <summary>
/// An immutable view of pressed keys and the distinct transitions that occurred in a frame.
/// </summary>
public sealed class InputSnapshot
{
    private readonly HashSet<InputKey> _pressedKeys;
    private readonly HashSet<InputKey> _pressedThisFrame;
    private readonly Dictionary<InputKey, long> _transitionSequences;
    private readonly Dictionary<InputKey, long> _keyDownSequences;
    private readonly Dictionary<InputKey, InputModifiers> _transitionModifiers;
    private readonly KeyTransition[] _transitions;

    public static InputSnapshot Empty { get; } = new([], []);

    public InputSnapshot(
        IEnumerable<InputKey> pressedKeys,
        IEnumerable<KeyTransition> transitions,
        IReadOnlyDictionary<InputKey, long>? transitionSequences = null,
        IReadOnlyDictionary<InputKey, long>? keyDownSequences = null)
    {
        ArgumentNullException.ThrowIfNull(pressedKeys);
        ArgumentNullException.ThrowIfNull(transitions);

        _pressedKeys = new HashSet<InputKey>(pressedKeys);
        _pressedThisFrame = new HashSet<InputKey>();
        _transitionSequences = new Dictionary<InputKey, long>();
        _keyDownSequences = new Dictionary<InputKey, long>();
        _transitionModifiers = new Dictionary<InputKey, InputModifiers>();
        var capturedTransitions = new List<KeyTransition>();

        if (transitionSequences is not null)
        {
            foreach (var (key, sequence) in transitionSequences)
            {
                _transitionSequences[key] = sequence;
            }
        }

        // The legacy sequence map represents the last distinct transition. When a
        // caller has no separate key-down history, it is also the best available
        // baseline for the last key-down history.
        var downSequenceSource = keyDownSequences ?? transitionSequences;
        if (downSequenceSource is not null)
        {
            foreach (var (key, sequence) in downSequenceSource)
            {
                _keyDownSequences[key] = sequence;
            }
        }

        foreach (var transition in transitions)
        {
            capturedTransitions.Add(transition);
            _transitionSequences[transition.Key] = transition.Sequence;
            if (transition.IsDown)
            {
                _pressedThisFrame.Add(transition.Key);
                _transitionModifiers[transition.Key] = transition.Modifiers;
                _keyDownSequences[transition.Key] = transition.Sequence;
            }
        }

        _transitions = capturedTransitions.ToArray();
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

    /// <summary>
    /// Gets the ordered distinct physical key state changes captured for this frame.
    /// </summary>
    public IReadOnlyList<KeyTransition> Transitions => _transitions;

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

    /// <summary>
    /// Gets the sequence of the most recent distinct physical key-down transition.
    /// Key-up transitions deliberately do not replace this value.
    /// </summary>
    public long KeyDownSequence(InputKey key) =>
        _keyDownSequences.TryGetValue(key, out var sequence) ? sequence : 0;

    /// <summary>
    /// Gets the normalized modifier flags captured with a key-down transition in this frame.
    /// </summary>
    public InputModifiers TransitionModifiers(InputKey key) =>
        _transitionModifiers.TryGetValue(key, out var modifiers) ? modifiers : InputModifiers.None;
}
