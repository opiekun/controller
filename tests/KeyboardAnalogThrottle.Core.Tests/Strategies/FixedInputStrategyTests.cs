using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Input;
using KeyboardAnalogThrottle.Core.Strategies;

namespace KeyboardAnalogThrottle.Core.Tests.Strategies;

public sealed class FixedInputStrategyTests
{
    [Fact]
    public void Selects_configured_maximum_level_for_bare_primary_binding()
    {
        var strategy = new FixedInputStrategy();
        var configuration = ChannelConfiguration.DefaultThrottle with
        {
            InitialLevel = .3,
            MaximumLevel = .85
        };

        var value = strategy.Update(new InputSnapshot([InputKey.W], []), .95, TimeSpan.FromSeconds(5), configuration);

        Assert.Equal(.85, value, 6);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Default_fixed_mode_uses_full_level_for_bare_primary_binding(bool throttle)
    {
        var strategy = new FixedInputStrategy();
        var configuration = throttle
            ? ChannelConfiguration.DefaultThrottle with { Mode = InputMode.Fixed }
            : ChannelConfiguration.DefaultBrake with { Mode = InputMode.Fixed };
        var primaryKey = throttle ? InputKey.W : InputKey.S;

        var value = strategy.Update(new InputSnapshot([primaryKey], []), 0d, TimeSpan.Zero, configuration);

        Assert.Equal(1d, value, 6);
    }

    [Fact]
    public void Selects_the_most_specific_configured_modifier_level()
    {
        var strategy = new FixedInputStrategy();
        var configuration = ChannelConfiguration.DefaultThrottle with
        {
            InitialLevel = .3,
            FixedLevels = new Dictionary<string, double>
            {
                ["Ctrl+W"] = .8,
                ["Ctrl+Shift+W"] = 1
            }
        };
        var input = new InputSnapshot([InputKey.W, InputKey.LeftControl, InputKey.RightShift], []);

        Assert.Equal(1, strategy.Update(input, .1, TimeSpan.Zero, configuration));
    }

    [Fact]
    public void Returns_zero_when_the_primary_binding_is_not_active()
    {
        var strategy = new FixedInputStrategy();

        Assert.Equal(0, strategy.Update(InputSnapshot.Empty, .6, TimeSpan.Zero, ChannelConfiguration.DefaultThrottle));
    }
}
