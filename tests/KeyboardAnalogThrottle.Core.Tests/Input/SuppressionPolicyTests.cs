using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Input;
using KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

namespace KeyboardAnalogThrottle.Core.Tests.Input;

public sealed class SuppressionPolicyTests
{
    [Fact]
    public void Suppresses_a_configured_primary_key_only_while_enabled_and_running()
    {
        var policy = new SuppressionPolicy(Configuration(suppressMappedKeys: true));
        var snapshot = InputSnapshot.FromPressed(InputKey.W);

        Assert.True(policy.ShouldSuppress(InputKey.W, snapshot, engineIsRunning: true));
        Assert.False(policy.ShouldSuppress(InputKey.W, snapshot, engineIsRunning: false));
        Assert.False(new SuppressionPolicy(Configuration(suppressMappedKeys: false))
            .ShouldSuppress(InputKey.W, snapshot, engineIsRunning: true));
    }

    [Fact]
    public void Does_not_suppress_standalone_modifiers_or_unrelated_keys()
    {
        var policy = new SuppressionPolicy(Configuration(suppressMappedKeys: true));
        var snapshot = InputSnapshot.FromPressed(InputKey.LeftControl, InputKey.W);

        Assert.False(policy.ShouldSuppress(InputKey.LeftControl, snapshot, engineIsRunning: true));
        Assert.False(policy.ShouldSuppress(InputKey.A, snapshot, engineIsRunning: true));
    }

    [Fact]
    public void Emergency_hotkey_is_never_suppressed()
    {
        var policy = new SuppressionPolicy(Configuration(suppressMappedKeys: true, brakePrimary: "F12"));
        var emergencySnapshot = InputSnapshot.FromPressed(InputKey.LeftControl, InputKey.LeftAlt, InputKey.F12);

        Assert.False(policy.ShouldSuppress(InputKey.F12, emergencySnapshot, engineIsRunning: true));
    }

    private static AppConfiguration Configuration(bool suppressMappedKeys, string brakePrimary = "S") => new()
    {
        Input = new InputConfiguration
        {
            SuppressMappedKeys = suppressMappedKeys,
            ThrottleCutBinding = "Space",
            EmergencyDisableBinding = "Ctrl+Alt+F12"
        },
        Throttle = new ChannelConfiguration { PrimaryBinding = "W" },
        Brake = new ChannelConfiguration { PrimaryBinding = brakePrimary },
        Ratchet = new RatchetConfiguration
        {
            IncreaseBinding = "PageUp",
            DecreaseBinding = "PageDown",
            ResetBinding = "Home"
        }
    };
}
