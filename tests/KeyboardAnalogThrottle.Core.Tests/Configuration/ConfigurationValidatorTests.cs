using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Emulation;

namespace KeyboardAnalogThrottle.Core.Tests.Configuration;

public sealed class ConfigurationValidatorTests
{
    public static IEnumerable<object[]> MissingSectionConfigurations()
    {
        var defaults = AppConfiguration.CreateDefault();

        yield return [defaults with { Controller = null! }, "Controller", "Controller configuration is required."];
        yield return [defaults with { Input = null! }, "Input", "Input configuration is required."];
        yield return [defaults with { Throttle = null! }, "Throttle", "Throttle configuration is required."];
        yield return [defaults with { Brake = null! }, "Brake", "Brake configuration is required."];
        yield return [defaults with { Ratchet = null! }, "Ratchet", "Ratchet configuration is required."];
        yield return [defaults with { Logging = null! }, "Logging", "Logging configuration is required."];
    }

    public static IEnumerable<object[]> NonFiniteValues()
    {
        yield return [double.NaN];
        yield return [double.PositiveInfinity];
        yield return [double.NegativeInfinity];
    }

    [Theory]
    [MemberData(nameof(MissingSectionConfigurations))]
    public void Rejects_missing_configuration_sections(
        AppConfiguration configuration,
        string propertyName,
        string message)
    {
        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.PropertyName == propertyName && error.Message == message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rejects_null_fixed_levels(bool throttle)
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = throttle
            ? defaults with { Throttle = defaults.Throttle with { FixedLevels = null! } }
            : defaults with { Brake = defaults.Brake with { FixedLevels = null! } };
        var channelName = throttle ? "Throttle" : "Brake";

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.PropertyName == $"{channelName}.FixedLevels" && error.Message == $"{channelName} fixed levels are required.");
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void Rejects_non_finite_rise_durations(double duration)
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Throttle = defaults.Throttle with { RiseSeconds = duration }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.Message == "Throttle rise duration must be greater than zero.");
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void Rejects_non_finite_fall_durations(double duration)
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Throttle = defaults.Throttle with { FallSeconds = duration }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.Message == "Throttle fall duration must be greater than zero.");
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void Rejects_non_finite_custom_exponents(double exponent)
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Throttle = defaults.Throttle with { CustomExponent = exponent }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.Message == "Throttle custom exponent must be greater than zero.");
    }

    [Theory]
    [InlineData(29)]
    [InlineData(251)]
    public void Rejects_update_rates_outside_the_supported_range(int updateRate)
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Controller = defaults.Controller with { UpdateRateHz = updateRate }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.Message == "Update rate must be between 30 and 250 Hz.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_non_positive_ramp_durations(double duration)
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Throttle = defaults.Throttle with { RiseSeconds = duration }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.Message == "Throttle rise duration must be greater than zero.");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Rejects_fixed_levels_outside_the_normalized_range(double level)
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Brake = defaults.Brake with
            {
                FixedLevels = new Dictionary<string, double> { ["Ctrl+S"] = level }
            }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.Message == "Fixed level 'Ctrl+S' must be between 0 and 1.");
    }

    [Fact]
    public void Rejects_equivalent_fixed_bindings()
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Throttle = defaults.Throttle with
            {
                FixedLevels = new Dictionary<string, double>
                {
                    ["Ctrl+Shift+W"] = .5,
                    ["Shift+Ctrl+W"] = .75
                }
            }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.Message == "Throttle fixed bindings contain equivalent entries: 'Ctrl+Shift+W' and 'Shift+Ctrl+W'.");
    }

    [Fact]
    public void Rejects_undefined_numeric_primary_keys_through_the_shared_binding_parser()
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Throttle = defaults.Throttle with { PrimaryBinding = "999" }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.PropertyName == "Throttle.PrimaryBinding" && error.Message == "Binding '999' is invalid.");
    }

    [Fact]
    public void Accepts_left_and_right_modifier_aliases_through_the_shared_binding_parser()
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Input = defaults.Input with { EmergencyDisableBinding = "RightControl+LeftAlt+F12" },
            Throttle = defaults.Throttle with
            {
                FixedLevels = new Dictionary<string, double>
                {
                    ["LeftShift+W"] = .5,
                    ["RightControl+W"] = 1
                }
            }
        };

        Assert.Empty(ConfigurationValidator.Validate(configuration));
    }

    [Fact]
    public void Default_configuration_is_valid()
    {
        Assert.Empty(ConfigurationValidator.Validate(AppConfiguration.CreateDefault()));
    }

    [Fact]
    public void Default_configuration_matches_the_racing_controls()
    {
        var configuration = AppConfiguration.CreateDefault();

        Assert.True(configuration.Input.SuppressMappedKeys);
        Assert.True(configuration.Controller.AllowSimultaneousThrottleAndBrake);
        Assert.Equal(ConflictMode.BrakeWins, configuration.Controller.ConflictMode);
        Assert.Equal(ThrottleCutMode.Hold, configuration.Input.ThrottleCutMode);
        Assert.Equal("W", configuration.Throttle.PrimaryBinding);
        Assert.Equal(InputMode.Ramp, configuration.Throttle.Mode);
        Assert.Equal(1.2d, configuration.Throttle.RiseSeconds);
        Assert.Equal(.45d, configuration.Throttle.FallSeconds);
        Assert.Equal(.08d, configuration.Throttle.InitialLevel);
        Assert.Equal(1d, configuration.Throttle.MaximumLevel);
        Assert.Equal("EaseOut", configuration.Throttle.Curve);
        AssertFixedLevels(configuration.Throttle, "W");
        Assert.Equal("S", configuration.Brake.PrimaryBinding);
        Assert.Equal(InputMode.Ramp, configuration.Brake.Mode);
        Assert.Equal(.3d, configuration.Brake.RiseSeconds);
        Assert.Equal(.2d, configuration.Brake.FallSeconds);
        Assert.Equal(0d, configuration.Brake.InitialLevel);
        Assert.Equal(1d, configuration.Brake.MaximumLevel);
        Assert.Equal("Linear", configuration.Brake.Curve);
        AssertFixedLevels(configuration.Brake, "S");
        Assert.Equal("W", configuration.Ratchet.IncreaseBinding);
        Assert.Equal("Q", configuration.Ratchet.DecreaseBinding);
        Assert.Equal("Space", configuration.Ratchet.ResetBinding);
        Assert.Equal(.1d, configuration.Ratchet.Step);
    }

    [Theory]
    [InlineData((ConflictMode)999)]
    [InlineData((ConflictMode)(-1))]
    public void Rejects_undefined_conflict_modes(ConflictMode conflictMode)
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Controller = defaults.Controller with { ConflictMode = conflictMode }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.PropertyName == "Controller.ConflictMode" && error.Message == "Conflict mode is invalid.");
    }

    [Theory]
    [InlineData((ThrottleCutMode)999)]
    [InlineData((ThrottleCutMode)(-1))]
    public void Rejects_undefined_throttle_cut_modes(ThrottleCutMode cutMode)
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Input = defaults.Input with { ThrottleCutMode = cutMode }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.PropertyName == "Input.ThrottleCutMode" && error.Message == "Throttle cut mode is invalid.");
    }

    [Fact]
    public void Rejects_undefined_channel_modes()
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Throttle = defaults.Throttle with { Mode = (InputMode)999 }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.PropertyName == "Throttle.Mode" && error.Message == "Throttle mode is invalid.");
    }

    [Fact]
    public void Rejects_unknown_output_curves()
    {
        var defaults = AppConfiguration.CreateDefault();
        var configuration = defaults with
        {
            Throttle = defaults.Throttle with { Curve = "Turbo" }
        };

        Assert.Contains(
            ConfigurationValidator.Validate(configuration),
            error => error.PropertyName == "Throttle.Curve" && error.Message == "Throttle curve is invalid.");
    }

    private static void AssertFixedLevels(ChannelConfiguration channel, string key)
    {
        Assert.Equal(.25d, channel.FixedLevels[$"Ctrl+{key}"]);
        Assert.Equal(.5d, channel.FixedLevels[$"Alt+{key}"]);
        Assert.Equal(.75d, channel.FixedLevels[$"Shift+{key}"]);
        Assert.Equal(1d, channel.FixedLevels[$"Ctrl+Shift+{key}"]);
    }
}
