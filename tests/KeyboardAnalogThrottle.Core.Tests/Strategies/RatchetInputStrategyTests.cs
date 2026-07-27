using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Input;
using KeyboardAnalogThrottle.Core.Strategies;

namespace KeyboardAnalogThrottle.Core.Tests.Strategies;

public sealed class RatchetInputStrategyTests
{
    [Fact]
    public void Consumes_each_distinct_down_transition_once_and_preserves_value_after_release()
    {
        var strategy = new RatchetInputStrategy(new RatchetConfiguration { Step = .25 });
        var increment = new InputSnapshot([InputKey.W], [new KeyTransition(InputKey.W, true, 1)]);

        var raised = strategy.Update(increment, 0, TimeSpan.Zero, ChannelConfiguration.DefaultThrottle);
        var held = strategy.Update(new InputSnapshot([InputKey.W], []), raised, TimeSpan.Zero, ChannelConfiguration.DefaultThrottle);
        var released = strategy.Update(InputSnapshot.Empty, held, TimeSpan.Zero, ChannelConfiguration.DefaultThrottle);

        Assert.Equal(.25, raised, 6);
        Assert.Equal(.25, held, 6);
        Assert.Equal(.25, released, 6);
    }

    [Fact]
    public void Applies_decrement_and_reset_transitions_and_clamps_to_channel_maximum()
    {
        var strategy = new RatchetInputStrategy(new RatchetConfiguration { Step = .4 });
        var channel = ChannelConfiguration.DefaultThrottle with { MaximumLevel = .7 };

        var clamped = strategy.Update(new InputSnapshot([InputKey.W], [new KeyTransition(InputKey.W, true, 1)]), .5, TimeSpan.Zero, channel);
        var decremented = strategy.Update(new InputSnapshot([InputKey.Q], [new KeyTransition(InputKey.Q, true, 2)]), clamped, TimeSpan.Zero, channel);
        var reset = strategy.Update(new InputSnapshot([InputKey.Space], [new KeyTransition(InputKey.Space, true, 3)]), decremented, TimeSpan.Zero, channel);

        Assert.Equal(.7, clamped, 6);
        Assert.Equal(.3, decremented, 6);
        Assert.Equal(0, reset);
    }

    [Fact]
    public void Uses_modifier_state_captured_at_a_ratchet_key_down_transition()
    {
        var strategy = new RatchetInputStrategy(new RatchetConfiguration { IncreaseBinding = "Ctrl+PageUp", Step = .25 });
        var tap = new InputSnapshot([], [new KeyTransition(InputKey.PageUp, true, 1, InputModifiers.Control)]);

        Assert.Equal(.25, strategy.Update(tap, 0, TimeSpan.Zero, ChannelConfiguration.DefaultThrottle), 6);
    }

    [Fact]
    public void Applies_every_qualifying_down_transition_in_the_order_it_was_observed()
    {
        var strategy = new RatchetInputStrategy(new RatchetConfiguration { Step = .25 });
        var transitions = new[]
        {
            new KeyTransition(InputKey.W, true, 1),
            new KeyTransition(InputKey.Q, true, 2),
            new KeyTransition(InputKey.W, true, 3)
        };
        var input = new InputSnapshot([InputKey.W, InputKey.Q], transitions);

        Assert.Equal(.5, strategy.Update(input, .25, TimeSpan.Zero, ChannelConfiguration.DefaultThrottle), 6);
    }
}
