using KeyboardAnalogThrottle.Core.Configuration;

namespace KeyboardAnalogThrottle.Core.Tests.Configuration;

public sealed class ConfigurationValidatorTests
{
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
    public void Default_configuration_is_valid()
    {
        Assert.Empty(ConfigurationValidator.Validate(AppConfiguration.CreateDefault()));
    }
}
