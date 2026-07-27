using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Bindings;

/// <summary>
/// Selects the most specific pressed fixed-level binding.
/// </summary>
public static class FixedBindingResolver
{
    public static double? Resolve(
        InputSnapshot snapshot,
        IReadOnlyDictionary<InputBinding, double> levels)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(levels);

        var binding = ResolveBinding(snapshot, levels);
        return binding is { } selected ? levels[selected] : null;
    }

    public static InputBinding? ResolveBinding(
        InputSnapshot snapshot,
        IReadOnlyDictionary<InputBinding, double> levels)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(levels);

        var found = false;
        var selectedModifierCount = -1;
        var selectedBinding = default(InputBinding);

        foreach (var (binding, _) in levels)
        {
            if (!binding.Matches(snapshot))
            {
                continue;
            }

            if (!found || binding.ModifierCount > selectedModifierCount ||
                (binding.ModifierCount == selectedModifierCount && binding.CompareCanonicalTo(selectedBinding) < 0))
            {
                found = true;
                selectedModifierCount = binding.ModifierCount;
                selectedBinding = binding;
            }
        }

        return found ? selectedBinding : null;
    }
}
