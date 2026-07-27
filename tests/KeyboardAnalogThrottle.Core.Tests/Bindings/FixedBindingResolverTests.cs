using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Tests.Bindings;

public sealed class FixedBindingResolverTests
{
    [Fact]
    public void Most_specific_matching_fixed_binding_wins()
    {
        var levels = new Dictionary<InputBinding, double>
        {
            [BindingParser.Parse("Ctrl+W")] = .25,
            [BindingParser.Parse("Shift+W")] = .75,
            [BindingParser.Parse("Ctrl+Shift+W")] = 1
        };
        var snapshot = InputSnapshot.FromPressed(InputKey.W, InputKey.LeftControl, InputKey.LeftShift);

        Assert.Equal(1, FixedBindingResolver.Resolve(snapshot, levels)!.Value);
    }

    [Fact]
    public void Returns_null_when_the_primary_key_is_not_held()
    {
        var levels = new Dictionary<InputBinding, double>
        {
            [BindingParser.Parse("Ctrl+W")] = 1
        };

        Assert.Null(FixedBindingResolver.Resolve(InputSnapshot.FromPressed(InputKey.LeftControl), levels));
    }

    [Fact]
    public void Treats_right_side_modifiers_as_their_normalized_flags()
    {
        var levels = new Dictionary<InputBinding, double>
        {
            [BindingParser.Parse("Alt+S")] = .5
        };
        var snapshot = InputSnapshot.FromPressed(InputKey.S, InputKey.RightAlt);

        Assert.Equal(.5, FixedBindingResolver.Resolve(snapshot, levels)!.Value);
    }

    [Fact]
    public void Breaks_equally_specific_matches_by_canonical_binding_text()
    {
        var levels = new Dictionary<InputBinding, double>
        {
            [BindingParser.Parse("Shift+W")] = .75,
            [BindingParser.Parse("Ctrl+W")] = .25
        };
        var snapshot = InputSnapshot.FromPressed(InputKey.W, InputKey.LeftControl, InputKey.LeftShift);

        Assert.Equal(.25, FixedBindingResolver.Resolve(snapshot, levels)!.Value);
    }

    [Fact]
    public void Prefers_the_lexically_first_canonical_text_when_equal_modifier_counts_match()
    {
        var levels = new Dictionary<InputBinding, double>
        {
            [BindingParser.Parse("Alt+Ctrl+W")] = .25,
            [BindingParser.Parse("Alt+Shift+W")] = .75
        };
        var snapshot = InputSnapshot.FromPressed(InputKey.W, InputKey.LeftControl, InputKey.LeftAlt, InputKey.LeftShift);

        Assert.Equal(.25, FixedBindingResolver.Resolve(snapshot, levels)!.Value);
    }
}
