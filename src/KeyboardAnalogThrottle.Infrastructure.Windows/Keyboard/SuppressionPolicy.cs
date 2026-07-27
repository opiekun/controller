using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

/// <summary>
/// A pure, immutable decision function for optional mapped-key suppression.
/// </summary>
public sealed class SuppressionPolicy
{
    private readonly bool _enabled;
    private readonly HashSet<InputKey> _mappedPrimaryKeys;
    private readonly InputBinding _emergencyBinding;

    public SuppressionPolicy(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _enabled = configuration.Input.SuppressMappedKeys;
        _mappedPrimaryKeys = [];
        _emergencyBinding = BindingParser.Parse(configuration.Input.EmergencyDisableBinding);

        AddBinding(configuration.Throttle.PrimaryBinding);
        AddBinding(configuration.Brake.PrimaryBinding);
        AddBinding(configuration.Input.ThrottleCutBinding);
        AddBinding(configuration.Ratchet.IncreaseBinding);
        AddBinding(configuration.Ratchet.DecreaseBinding);
        AddBinding(configuration.Ratchet.ResetBinding);

        foreach (var binding in configuration.Throttle.FixedLevels.Keys)
        {
            AddBinding(binding);
        }

        foreach (var binding in configuration.Brake.FixedLevels.Keys)
        {
            AddBinding(binding);
        }
    }

    public bool ShouldSuppress(InputKey key, InputSnapshot snapshot, bool engineIsRunning)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ShouldSuppress(key, snapshot.Modifiers, engineIsRunning);
    }

    public bool ShouldSuppress(InputKey key, InputModifiers modifiers, bool engineIsRunning) =>
        _enabled &&
            engineIsRunning &&
            !IsModifier(key) &&
            _mappedPrimaryKeys.Contains(key) &&
            !IsCompleteEmergencyBinding(key, modifiers);

    public static bool ShouldSuppress(
        InputKey key,
        InputSnapshot snapshot,
        AppConfiguration configuration,
        bool engineIsRunning) =>
        new SuppressionPolicy(configuration).ShouldSuppress(key, snapshot, engineIsRunning);

    public static bool ShouldSuppress(InputSnapshot snapshot, AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var key = snapshot.Transitions.Count == 0 ? InputKey.None : snapshot.Transitions[^1].Key;
        return new SuppressionPolicy(configuration).ShouldSuppress(key, snapshot, engineIsRunning: true);
    }

    private void AddBinding(string binding) => _mappedPrimaryKeys.Add(BindingParser.Parse(binding).Primary);

    private bool IsCompleteEmergencyBinding(InputKey key, InputModifiers modifiers) =>
        key == _emergencyBinding.Primary &&
        (modifiers & _emergencyBinding.Modifiers) == _emergencyBinding.Modifiers;

    private static bool IsModifier(InputKey key) => key is
        InputKey.LeftControl or InputKey.RightControl or
        InputKey.LeftAlt or InputKey.RightAlt or
        InputKey.LeftShift or InputKey.RightShift;
}
