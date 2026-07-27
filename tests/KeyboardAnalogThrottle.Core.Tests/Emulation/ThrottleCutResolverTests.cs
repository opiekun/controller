using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Tests.Emulation;

public sealed class ThrottleCutResolverTests
{
    [Fact]
    public void Hold_cut_returns_zero_only_while_its_binding_is_active()
    {
        var resolver = new ThrottleCutResolver();
        var binding = BindingParser.Parse("Space");

        Assert.Equal(0, resolver.Resolve(new InputSnapshot([InputKey.Space], []), binding, .8, toggle: false));
        Assert.Equal(.8, resolver.Resolve(InputSnapshot.Empty, binding, .8, toggle: false));
    }

    [Fact]
    public void Toggle_cut_changes_only_on_a_distinct_key_down_transition()
    {
        var resolver = new ThrottleCutResolver();
        var binding = BindingParser.Parse("Space");
        var down = new InputSnapshot([InputKey.Space], [new KeyTransition(InputKey.Space, true, 1)]);
        var held = new InputSnapshot([InputKey.Space], []);

        Assert.Equal(0, resolver.Resolve(down, binding, .8, toggle: true));
        Assert.Equal(0, resolver.Resolve(held, binding, .8, toggle: true));
        Assert.Equal(.8, resolver.Resolve(new InputSnapshot([InputKey.Space], [new KeyTransition(InputKey.Space, true, 2)]), binding, .8, toggle: true));
    }

    [Fact]
    public void Toggle_cut_consumes_a_down_transition_even_when_the_key_was_released_before_sampling()
    {
        var resolver = new ThrottleCutResolver();
        var binding = BindingParser.Parse("Space");
        var tap = new InputSnapshot([], [new KeyTransition(InputKey.Space, true, 1)]);

        Assert.Equal(0, resolver.Resolve(tap, binding, .8, toggle: true));
    }

    [Fact]
    public void Toggle_cut_uses_modifier_state_captured_at_the_down_transition()
    {
        var resolver = new ThrottleCutResolver();
        var binding = BindingParser.Parse("Ctrl+Space");
        var tap = new InputSnapshot([], [new KeyTransition(InputKey.Space, true, 1, InputModifiers.Control)]);

        Assert.Equal(0, resolver.Resolve(tap, binding, .8, toggle: true));
    }

    [Fact]
    public void Toggle_cut_toggles_for_each_qualifying_down_transition_between_samples()
    {
        var resolver = new ThrottleCutResolver();
        var binding = BindingParser.Parse("Space");
        var input = new InputSnapshot([], [
            new KeyTransition(InputKey.Space, true, 1),
            new KeyTransition(InputKey.Space, false, 2),
            new KeyTransition(InputKey.Space, true, 3)]);

        Assert.Equal(.8, resolver.Resolve(input, binding, .8, toggle: true));
    }
}
