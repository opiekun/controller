using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Bindings;

/// <summary>
/// Parses configuration binding text into a normalized structural binding.
/// </summary>
public static class BindingParser
{
    public static InputBinding Parse(string binding)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            throw new ArgumentException("A binding is required.", nameof(binding));
        }

        var modifiers = InputModifiers.None;
        var primary = InputKey.None;

        foreach (var token in binding.Split('+', StringSplitOptions.TrimEntries))
        {
            if (token.Length == 0 || token.Any(char.IsWhiteSpace))
            {
                throw InvalidBinding(binding);
            }

            if (TryParseModifier(token, out var modifier))
            {
                if ((modifiers & modifier) != 0)
                {
                    throw InvalidBinding(binding);
                }

                modifiers |= modifier;
                continue;
            }

            if (primary != InputKey.None || !TryParsePrimary(token, out primary))
            {
                throw InvalidBinding(binding);
            }
        }

        if (primary == InputKey.None)
        {
            throw InvalidBinding(binding);
        }

        return new InputBinding(primary, modifiers);
    }

    private static bool TryParseModifier(string token, out InputModifiers modifier)
    {
        modifier = token.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" or "LEFTCONTROL" or "RIGHTCONTROL" => InputModifiers.Control,
            "ALT" or "LEFTALT" or "RIGHTALT" => InputModifiers.Alt,
            "SHIFT" or "LEFTSHIFT" or "RIGHTSHIFT" => InputModifiers.Shift,
            _ => InputModifiers.None
        };
        return modifier != InputModifiers.None;
    }

    private static bool TryParsePrimary(string token, out InputKey key)
    {
        key = InputKey.None;
        var normalized = token.ToUpperInvariant();

        if (normalized.Length == 1 && normalized[0] is >= '0' and <= '9')
        {
            key = InputKey.D0 + (normalized[0] - '0');
            return true;
        }

        if (!Enum.TryParse<InputKey>(token, true, out key) || !Enum.IsDefined(key) || key is InputKey.None or
            InputKey.LeftControl or InputKey.RightControl or InputKey.LeftAlt or InputKey.RightAlt or InputKey.LeftShift or InputKey.RightShift)
        {
            key = InputKey.None;
            return false;
        }

        return true;
    }

    private static ArgumentException InvalidBinding(string binding) =>
        new($"Binding '{binding}' is invalid.", nameof(binding));
}
