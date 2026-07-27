using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Tests.Emulation;

public sealed class ConflictResolverTests
{
    [Theory]
    [InlineData(ConflictMode.BrakeWins, 0d, .6d)]
    [InlineData(ConflictMode.ThrottleWins, .8d, 0d)]
    [InlineData(ConflictMode.CancelBoth, 0d, 0d)]
    public void Applies_the_configured_rule_when_both_base_keys_are_pressed(ConflictMode mode, double expectedThrottle, double expectedBrake)
    {
        var input = new InputSnapshot(
            [InputKey.W, InputKey.S],
            [new KeyTransition(InputKey.W, true, 1), new KeyTransition(InputKey.S, true, 2)]);

        var result = ConflictResolver.Resolve(input, .8, .6, InputKey.W, InputKey.S, mode);

        Assert.Equal(expectedThrottle, result.Throttle, 6);
        Assert.Equal(expectedBrake, result.Brake, 6);
    }

    [Fact]
    public void Last_pressed_wins_from_snapshot_sequences_regardless_of_strategy_call_order()
    {
        var input = new InputSnapshot(
            [InputKey.W, InputKey.S],
            [new KeyTransition(InputKey.S, true, 4), new KeyTransition(InputKey.W, true, 9)]);

        var result = ConflictResolver.Resolve(input, .8, .6, InputKey.W, InputKey.S, ConflictMode.LastPressedWins);

        Assert.Equal(.8, result.Throttle, 6);
        Assert.Equal(0, result.Brake, 6);
    }

    [Fact]
    public void Last_pressed_wins_while_both_keys_remain_held_after_their_transition_frame()
    {
        var input = new InputSnapshot(
            [InputKey.W, InputKey.S],
            [],
            new Dictionary<InputKey, long>
            {
                [InputKey.W] = 9,
                [InputKey.S] = 4
            });

        var result = ConflictResolver.Resolve(input, .8, .6, InputKey.W, InputKey.S, ConflictMode.LastPressedWins);

        Assert.Equal(.8, result.Throttle, 6);
        Assert.Equal(0, result.Brake, 6);
    }

    [Fact]
    public void Leaves_values_unchanged_when_simultaneous_input_is_enabled_or_not_physically_simultaneous()
    {
        var onlyThrottlePressed = new InputSnapshot([InputKey.W], [new KeyTransition(InputKey.W, true, 1)]);

        var notSimultaneous = ConflictResolver.Resolve(onlyThrottlePressed, .8, .6, InputKey.W, InputKey.S, ConflictMode.BrakeWins);
        var enabled = ConflictResolver.Resolve(onlyThrottlePressed, .8, .6, InputKey.W, InputKey.S, ConflictMode.BrakeWins, simultaneousInputEnabled: true);

        Assert.Equal((.8, .6), notSimultaneous);
        Assert.Equal((.8, .6), enabled);
    }
}
