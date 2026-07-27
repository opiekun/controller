using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Tests.Bindings;

public sealed class BindingParserTests
{
    [Fact]
    public void Parses_modifiers_in_any_order_into_a_canonical_binding()
    {
        var binding = BindingParser.Parse("Shift+Ctrl+W");

        Assert.Equal(InputKey.W, binding.Primary);
        Assert.Equal(InputModifiers.Control | InputModifiers.Shift, binding.Modifiers);
        Assert.Equal("Ctrl+Shift+W", binding.ToString());
    }

    [Theory]
    [InlineData("Ctrl+Ctrl+W")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl++W")]
    [InlineData("Ctrl+BadKey")]
    [InlineData("999")]
    [InlineData("-1")]
    [InlineData("W+S")]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_duplicate_modifier_modifier_only_malformed_and_unknown_bindings(string binding) =>
        Assert.Throws<ArgumentException>(() => BindingParser.Parse(binding));

    [Theory]
    [InlineData("Control+W", "Ctrl+W")]
    [InlineData("LeftControl+W", "Ctrl+W")]
    [InlineData("RightShift+PageDown", "Shift+PageDown")]
    [InlineData("Space", "Space")]
    [InlineData("F12", "F12")]
    public void Normalizes_supported_key_and_modifier_aliases(string text, string expected) =>
        Assert.Equal(expected, BindingParser.Parse(text).ToString());
}
