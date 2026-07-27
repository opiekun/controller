using System.Diagnostics;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

/// <summary>
/// Synchronizes physical key state and drains ordered transitions to engine snapshots.
/// </summary>
public sealed class KeyboardStateStore
{
    private readonly object _gate = new();
    private readonly HashSet<InputKey> _pressedKeys = [];
    private readonly List<KeyTransition> _pendingTransitions = [];
    private readonly Dictionary<InputKey, long> _transitionSequences = [];
    private long _sequence;
    private long _lastHeartbeatTimestamp;
    private InputHealth _health = InputHealth.Synchronizing;

    public InputHealth Health
    {
        get
        {
            lock (_gate)
            {
                return _health;
            }
        }
    }

    public long LastHeartbeatTimestamp
    {
        get
        {
            lock (_gate)
            {
                return _lastHeartbeatTimestamp;
            }
        }
    }

    public KeyStateChangedEventArgs? ApplyDown(InputKey key) => Apply(key, isDown: true);

    public KeyStateChangedEventArgs? ApplyUp(InputKey key) => Apply(key, isDown: false);

    public InputSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var snapshot = new InputSnapshot(_pressedKeys, _pendingTransitions, _transitionSequences);
            _pendingTransitions.Clear();
            return snapshot;
        }
    }

    /// <summary>
    /// Reads the current state without consuming its transition stream.
    /// </summary>
    public InputSnapshot PeekSnapshot()
    {
        lock (_gate)
        {
            return new InputSnapshot(_pressedKeys, _pendingTransitions, _transitionSequences);
        }
    }

    public void SetHealth(InputHealth health)
    {
        lock (_gate)
        {
            _health = health;
            _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
        }
    }

    public void SynchronizeModifiers(InputModifiers modifiers)
    {
        lock (_gate)
        {
            SetModifier(InputKey.LeftControl, InputModifiers.Control);
            SetModifier(InputKey.LeftAlt, InputModifiers.Alt);
            SetModifier(InputKey.LeftShift, InputModifiers.Shift);
            _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();

            void SetModifier(InputKey key, InputModifiers flag)
            {
                if ((modifiers & flag) != 0)
                {
                    _pressedKeys.Add(key);
                }
                else
                {
                    _pressedKeys.Remove(key);
                }
            }
        }
    }

    public void Clear(InputHealth health = InputHealth.Synchronizing)
    {
        lock (_gate)
        {
            _pressedKeys.Clear();
            _pendingTransitions.Clear();
            _transitionSequences.Clear();
            _sequence = 0;
            _health = health;
            _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private KeyStateChangedEventArgs? Apply(InputKey key, bool isDown)
    {
        if (key == InputKey.None)
        {
            return null;
        }

        lock (_gate)
        {
            _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
            _health = InputHealth.Healthy;

            var changed = isDown ? _pressedKeys.Add(key) : _pressedKeys.Remove(key);
            if (!changed)
            {
                return null;
            }

            var transition = new KeyTransition(key, isDown, ++_sequence, CurrentModifiers());
            _transitionSequences[key] = transition.Sequence;
            _pendingTransitions.Add(transition);
            return new KeyStateChangedEventArgs(transition);
        }
    }

    private InputModifiers CurrentModifiers()
    {
        var modifiers = InputModifiers.None;
        if (_pressedKeys.Contains(InputKey.LeftControl) || _pressedKeys.Contains(InputKey.RightControl)) modifiers |= InputModifiers.Control;
        if (_pressedKeys.Contains(InputKey.LeftAlt) || _pressedKeys.Contains(InputKey.RightAlt)) modifiers |= InputModifiers.Alt;
        if (_pressedKeys.Contains(InputKey.LeftShift) || _pressedKeys.Contains(InputKey.RightShift)) modifiers |= InputModifiers.Shift;
        return modifiers;
    }
}
