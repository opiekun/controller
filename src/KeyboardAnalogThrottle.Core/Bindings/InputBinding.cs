using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Bindings;

/// <summary>
/// A primary key with an optional normalized modifier combination.
/// </summary>
public readonly record struct InputBinding(InputKey Primary, InputModifiers Modifiers)
{
    public int ModifierCount =>
        ((Modifiers & InputModifiers.Control) != 0 ? 1 : 0) +
        ((Modifiers & InputModifiers.Alt) != 0 ? 1 : 0) +
        ((Modifiers & InputModifiers.Shift) != 0 ? 1 : 0);

    public bool Matches(InputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Primary != InputKey.None && snapshot.IsPressed(Primary) && (snapshot.Modifiers & Modifiers) == Modifiers;
    }

    public override string ToString() => string.Concat(ModifierPrefix(Modifiers), FormatPrimary(Primary));

    internal int CompareCanonicalTo(InputBinding other)
    {
        var modifierComparison = string.CompareOrdinal(ModifierPrefix(Modifiers), ModifierPrefix(other.Modifiers));
        return modifierComparison != 0
            ? modifierComparison
            : string.CompareOrdinal(FormatPrimary(Primary), FormatPrimary(other.Primary));
    }

    private static string ModifierPrefix(InputModifiers modifiers) => modifiers switch
    {
        InputModifiers.None => "",
        InputModifiers.Alt => "Alt+",
        InputModifiers.Control => "Ctrl+",
        InputModifiers.Shift => "Shift+",
        InputModifiers.Control | InputModifiers.Alt => "Alt+Ctrl+",
        InputModifiers.Alt | InputModifiers.Shift => "Alt+Shift+",
        InputModifiers.Control | InputModifiers.Shift => "Ctrl+Shift+",
        InputModifiers.Control | InputModifiers.Alt | InputModifiers.Shift => "Alt+Ctrl+Shift+",
        _ => ""
    };

    private static string FormatPrimary(InputKey key) => key switch
    {
        InputKey.A => "A", InputKey.B => "B", InputKey.C => "C", InputKey.D => "D", InputKey.E => "E",
        InputKey.F => "F", InputKey.G => "G", InputKey.H => "H", InputKey.I => "I", InputKey.J => "J",
        InputKey.K => "K", InputKey.L => "L", InputKey.M => "M", InputKey.N => "N", InputKey.O => "O",
        InputKey.P => "P", InputKey.Q => "Q", InputKey.R => "R", InputKey.S => "S", InputKey.T => "T",
        InputKey.U => "U", InputKey.V => "V", InputKey.W => "W", InputKey.X => "X", InputKey.Y => "Y",
        InputKey.Z => "Z",
        InputKey.D0 => "0", InputKey.D1 => "1", InputKey.D2 => "2", InputKey.D3 => "3", InputKey.D4 => "4",
        InputKey.D5 => "5", InputKey.D6 => "6", InputKey.D7 => "7", InputKey.D8 => "8", InputKey.D9 => "9",
        InputKey.F1 => "F1", InputKey.F2 => "F2", InputKey.F3 => "F3", InputKey.F4 => "F4", InputKey.F5 => "F5",
        InputKey.F6 => "F6", InputKey.F7 => "F7", InputKey.F8 => "F8", InputKey.F9 => "F9", InputKey.F10 => "F10",
        InputKey.F11 => "F11", InputKey.F12 => "F12",
        InputKey.Space => "Space", InputKey.PageUp => "PageUp", InputKey.PageDown => "PageDown", InputKey.Home => "Home",
        InputKey.LeftControl => "LeftControl", InputKey.RightControl => "RightControl",
        InputKey.LeftAlt => "LeftAlt", InputKey.RightAlt => "RightAlt",
        InputKey.LeftShift => "LeftShift", InputKey.RightShift => "RightShift",
        _ => "None"
    };
}
