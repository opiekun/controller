using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Input;
using KeyboardAnalogThrottle.Core.Strategies;

namespace KeyboardAnalogThrottle.Core.Tests.Strategies;

public sealed class RampInputStrategyTests
{
    public static IEnumerable<object[]> UpdateRates()
    {
        yield return [30];
        yield return [60];
        yield return [120];
        yield return [240];
    }

    [Theory]
    [MemberData(nameof(UpdateRates))]
    public void Reaches_full_value_over_configured_rise_time(int hertz)
    {
        var strategy = new RampInputStrategy();
        var configuration = ChannelConfiguration.DefaultThrottle with { InitialLevel = 0, RiseSeconds = 1 };
        var value = 0d;
        var pressed = new InputSnapshot([InputKey.W], [new KeyTransition(InputKey.W, true, 1)]);
        var held = new InputSnapshot([InputKey.W], []);

        value = strategy.Update(pressed, value, TimeSpan.FromSeconds(1d / hertz), configuration);
        for (var frame = 1; frame < hertz * 12 / 10; frame++)
            value = strategy.Update(held, value, TimeSpan.FromSeconds(1d / hertz), configuration);

        Assert.Equal(1d, value, 6);
    }

    [Fact]
    public void Applies_the_initial_minimum_only_on_a_distinct_primary_key_down()
    {
        var strategy = new RampInputStrategy();
        var configuration = ChannelConfiguration.DefaultThrottle with { InitialLevel = .25 };
        var heldWithoutTransition = new InputSnapshot([InputKey.W], []);
        var keyDown = new InputSnapshot([InputKey.W], [new KeyTransition(InputKey.W, true, 3)]);

        Assert.Equal(0, strategy.Update(heldWithoutTransition, 0, TimeSpan.Zero, configuration));
        Assert.Equal(.25, strategy.Update(keyDown, 0, TimeSpan.Zero, configuration));
    }

    [Fact]
    public void Falls_to_zero_at_the_configured_rate_after_the_primary_is_released()
    {
        var strategy = new RampInputStrategy();
        var configuration = ChannelConfiguration.DefaultThrottle with { FallSeconds = .5 };

        var value = strategy.Update(InputSnapshot.Empty, 1, TimeSpan.FromSeconds(.25), configuration);

        Assert.Equal(.5, value, 6);
    }
}
