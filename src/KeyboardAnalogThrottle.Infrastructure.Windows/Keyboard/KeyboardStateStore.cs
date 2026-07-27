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
    private long _captureGeneration;
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

    /// <summary>
    /// Starts a capture session. Callbacks must present this generation before they may mutate state.
    /// </summary>
    public long BeginCapture()
    {
        lock (_gate)
        {
            ClearUnsafe(InputHealth.Synchronizing);
            return ++_captureGeneration;
        }
    }

    /// <summary>
    /// Invalidates all callbacks from the current capture session before clearing keyboard state.
    /// </summary>
    public void StopCapture()
    {
        lock (_gate)
        {
            ++_captureGeneration;
            ClearUnsafe(InputHealth.Unavailable);
        }
    }

    public bool IsCurrentCapture(long captureGeneration)
    {
        lock (_gate)
        {
            return captureGeneration != 0 && captureGeneration == _captureGeneration;
        }
    }

    public KeyStateChangedEventArgs? TryApplyDown(InputKey key, long captureGeneration) =>
        Apply(key, isDown: true, captureGeneration);

    public KeyStateChangedEventArgs? TryApplyUp(InputKey key, long captureGeneration) =>
        Apply(key, isDown: false, captureGeneration);

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
            ClearUnsafe(health);
        }
    }

    private KeyStateChangedEventArgs? Apply(InputKey key, bool isDown, long? captureGeneration = null)
    {
        if (key == InputKey.None)
        {
            return null;
        }

        lock (_gate)
        {
            if (captureGeneration is not null && captureGeneration.Value != _captureGeneration)
            {
                return null;
            }

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

    private void ClearUnsafe(InputHealth health)
    {
        _pressedKeys.Clear();
        _pendingTransitions.Clear();
        _transitionSequences.Clear();
        _sequence = 0;
        _health = health;
        _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
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
