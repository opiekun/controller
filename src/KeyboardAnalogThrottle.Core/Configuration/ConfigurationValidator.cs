using KeyboardAnalogThrottle.Core.Bindings;

namespace KeyboardAnalogThrottle.Core.Configuration;

public sealed record ConfigurationValidationError(string PropertyName, string Message);

/// <summary>
/// Performs validation that is safe to execute before any Windows input or controller resources are created.
/// </summary>
public static class ConfigurationValidator
{
    public static IReadOnlyList<ConfigurationValidationError> Validate(AppConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var errors = new List<ConfigurationValidationError>();
        ValidateRequiredSection("Controller", configuration.Controller, errors, ValidateController);
        ValidateRequiredSection("Input", configuration.Input, errors, ValidateInput);
        ValidateRequiredSection("Throttle", configuration.Throttle, errors, (channel, validationErrors) => ValidateChannel("Throttle", channel, validationErrors));
        ValidateRequiredSection("Brake", configuration.Brake, errors, (channel, validationErrors) => ValidateChannel("Brake", channel, validationErrors));
        ValidateRequiredSection("Ratchet", configuration.Ratchet, errors, ValidateRatchet);
        ValidateRequiredSection("Logging", configuration.Logging, errors, ValidateLogging);
        return errors;
    }

    private static void ValidateRequiredSection<T>(
        string sectionName,
        T? section,
        ICollection<ConfigurationValidationError> errors,
        Action<T, ICollection<ConfigurationValidationError>> validate)
        where T : class
    {
        if (section is null)
        {
            errors.Add(new(sectionName, $"{sectionName} configuration is required."));
            return;
        }

        validate(section, errors);
    }

    private static void ValidateController(ControllerConfiguration controller, ICollection<ConfigurationValidationError> errors)
    {
        if (controller.UpdateRateHz is < 30 or > 250)
        {
            errors.Add(new("Controller.UpdateRateHz", "Update rate must be between 30 and 250 Hz."));
        }

        if (controller.MaximumFrameDeltaMilliseconds is < 1 or > 1_000)
        {
            errors.Add(new("Controller.MaximumFrameDeltaMilliseconds", "Maximum frame delta must be between 1 and 1000 milliseconds."));
        }

        if (controller.InputLossTimeoutMilliseconds is < 1 or > 60_000)
        {
            errors.Add(new("Controller.InputLossTimeoutMilliseconds", "Input-loss timeout must be between 1 and 60000 milliseconds."));
        }
    }

    private static void ValidateInput(InputConfiguration input, ICollection<ConfigurationValidationError> errors)
    {
        ValidateBinding("Input.ThrottleCutBinding", input.ThrottleCutBinding, errors);
        ValidateBinding("Input.EmergencyDisableBinding", input.EmergencyDisableBinding, errors);
    }

    private static void ValidateChannel(string channelName, ChannelConfiguration channel, ICollection<ConfigurationValidationError> errors)
    {
        ValidateBinding($"{channelName}.PrimaryBinding", channel.PrimaryBinding, errors);

        if (!double.IsFinite(channel.RiseSeconds) || channel.RiseSeconds <= 0)
        {
            errors.Add(new($"{channelName}.RiseSeconds", $"{channelName} rise duration must be greater than zero."));
        }

        if (!double.IsFinite(channel.FallSeconds) || channel.FallSeconds <= 0)
        {
            errors.Add(new($"{channelName}.FallSeconds", $"{channelName} fall duration must be greater than zero."));
        }

        ValidateNormalizedLevel($"{channelName}.InitialLevel", $"{channelName} initial level", channel.InitialLevel, errors);
        ValidateNormalizedLevel($"{channelName}.MaximumLevel", $"{channelName} maximum level", channel.MaximumLevel, errors);

        if (channel.InitialLevel > channel.MaximumLevel)
        {
            errors.Add(new($"{channelName}.InitialLevel", $"{channelName} initial level must not exceed the maximum level."));
        }

        if (!double.IsFinite(channel.CustomExponent) || channel.CustomExponent <= 0)
        {
            errors.Add(new($"{channelName}.CustomExponent", $"{channelName} custom exponent must be greater than zero."));
        }

        ValidateFixedLevels(channelName, channel.FixedLevels, errors);
    }

    private static void ValidateFixedLevels(
        string channelName,
        IReadOnlyDictionary<string, double>? fixedLevels,
        ICollection<ConfigurationValidationError> errors)
    {
        if (fixedLevels is null)
        {
            errors.Add(new($"{channelName}.FixedLevels", $"{channelName} fixed levels are required."));
            return;
        }

        var structuralBindings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (binding, level) in fixedLevels)
        {
            ValidateNormalizedLevel($"{channelName}.FixedLevels", $"Fixed level '{binding}'", level, errors);

            if (!TryParseStructuralBinding(binding, out var canonicalBinding))
            {
                errors.Add(new($"{channelName}.FixedLevels", $"{channelName} fixed binding '{binding}' is invalid."));
                continue;
            }

            if (structuralBindings.TryGetValue(canonicalBinding, out var equivalentBinding))
            {
                errors.Add(new(
                    $"{channelName}.FixedLevels",
                    $"{channelName} fixed bindings contain equivalent entries: '{equivalentBinding}' and '{binding}'."));
                continue;
            }

            structuralBindings.Add(canonicalBinding, binding);
        }
    }

    private static void ValidateRatchet(RatchetConfiguration ratchet, ICollection<ConfigurationValidationError> errors)
    {
        ValidateBinding("Ratchet.IncreaseBinding", ratchet.IncreaseBinding, errors);
        ValidateBinding("Ratchet.DecreaseBinding", ratchet.DecreaseBinding, errors);
        ValidateBinding("Ratchet.ResetBinding", ratchet.ResetBinding, errors);
        ValidateNormalizedLevel("Ratchet.Step", "Ratchet step", ratchet.Step, errors, allowZero: false);
    }

    private static void ValidateLogging(LoggingConfiguration logging, ICollection<ConfigurationValidationError> errors)
    {
        if (logging.RetainedFileCountLimit <= 0)
        {
            errors.Add(new("Logging.RetainedFileCountLimit", "Retained log file count must be greater than zero."));
        }
    }

    private static void ValidateBinding(string propertyName, string binding, ICollection<ConfigurationValidationError> errors)
    {
        if (!TryParseStructuralBinding(binding, out _))
        {
            errors.Add(new(propertyName, $"Binding '{binding}' is invalid."));
        }
    }

    private static void ValidateNormalizedLevel(
        string propertyName,
        string displayName,
        double level,
        ICollection<ConfigurationValidationError> errors,
        bool allowZero = true)
    {
        var isValid = double.IsFinite(level) && (allowZero ? level is >= 0 and <= 1 : level is > 0 and <= 1);
        if (!isValid)
        {
            errors.Add(new(propertyName, $"{displayName} must be between {(allowZero ? "0" : "greater than 0")} and 1."));
        }
    }

    private static bool TryParseStructuralBinding(string? binding, out string canonicalBinding)
    {
        canonicalBinding = string.Empty;
        if (string.IsNullOrWhiteSpace(binding))
        {
            return false;
        }

        try
        {
            canonicalBinding = BindingParser.Parse(binding).ToString();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
