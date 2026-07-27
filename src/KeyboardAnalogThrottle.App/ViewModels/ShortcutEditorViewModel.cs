using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;

namespace KeyboardAnalogThrottle.App.ViewModels;

/// <summary>
/// Holds editable keyboard shortcut text separately from the active configuration snapshot.
/// </summary>
public sealed class ShortcutEditorViewModel : ObservableObject
{
    private readonly IConfigurationService _configurationService;
    private readonly AppConfiguration _sourceConfiguration;
    private string _throttlePrimaryBinding;
    private string _brakePrimaryBinding;
    private string _throttleCutBinding;
    private string _emergencyDisableBinding;
    private string _ratchetIncreaseBinding;
    private string _ratchetDecreaseBinding;
    private string _ratchetResetBinding;
    private string _validationMessage = string.Empty;

    public ShortcutEditorViewModel(IConfigurationService configurationService)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _sourceConfiguration = configurationService.Current;

        ThrottleFixedLevels = CreateEntries(_sourceConfiguration.Throttle.FixedLevels);
        BrakeFixedLevels = CreateEntries(_sourceConfiguration.Brake.FixedLevels);
        _throttlePrimaryBinding = _sourceConfiguration.Throttle.PrimaryBinding;
        _brakePrimaryBinding = _sourceConfiguration.Brake.PrimaryBinding;
        _throttleCutBinding = _sourceConfiguration.Input.ThrottleCutBinding;
        _emergencyDisableBinding = _sourceConfiguration.Input.EmergencyDisableBinding;
        _ratchetIncreaseBinding = _sourceConfiguration.Ratchet.IncreaseBinding;
        _ratchetDecreaseBinding = _sourceConfiguration.Ratchet.DecreaseBinding;
        _ratchetResetBinding = _sourceConfiguration.Ratchet.ResetBinding;
    }

    public string ThrottlePrimaryBinding
    {
        get => _throttlePrimaryBinding;
        set => SetProperty(ref _throttlePrimaryBinding, value);
    }

    public string ThrottleFixed25Binding
    {
        get => GetBindingAtLevel(ThrottleFixedLevels, .25d);
        set => SetBindingAtLevel(ThrottleFixedLevels, .25d, value, nameof(ThrottleFixed25Binding));
    }

    public string ThrottleFixed50Binding
    {
        get => GetBindingAtLevel(ThrottleFixedLevels, .5d);
        set => SetBindingAtLevel(ThrottleFixedLevels, .5d, value, nameof(ThrottleFixed50Binding));
    }

    public string ThrottleFixed75Binding
    {
        get => GetBindingAtLevel(ThrottleFixedLevels, .75d);
        set => SetBindingAtLevel(ThrottleFixedLevels, .75d, value, nameof(ThrottleFixed75Binding));
    }

    public string ThrottleFixed100Binding
    {
        get => GetBindingAtLevel(ThrottleFixedLevels, 1d);
        set => SetBindingAtLevel(ThrottleFixedLevels, 1d, value, nameof(ThrottleFixed100Binding));
    }

    public string BrakePrimaryBinding
    {
        get => _brakePrimaryBinding;
        set => SetProperty(ref _brakePrimaryBinding, value);
    }

    public string BrakeFixed25Binding
    {
        get => GetBindingAtLevel(BrakeFixedLevels, .25d);
        set => SetBindingAtLevel(BrakeFixedLevels, .25d, value, nameof(BrakeFixed25Binding));
    }

    public string BrakeFixed50Binding
    {
        get => GetBindingAtLevel(BrakeFixedLevels, .5d);
        set => SetBindingAtLevel(BrakeFixedLevels, .5d, value, nameof(BrakeFixed50Binding));
    }

    public string BrakeFixed75Binding
    {
        get => GetBindingAtLevel(BrakeFixedLevels, .75d);
        set => SetBindingAtLevel(BrakeFixedLevels, .75d, value, nameof(BrakeFixed75Binding));
    }

    public string BrakeFixed100Binding
    {
        get => GetBindingAtLevel(BrakeFixedLevels, 1d);
        set => SetBindingAtLevel(BrakeFixedLevels, 1d, value, nameof(BrakeFixed100Binding));
    }

    public IReadOnlyList<FixedLevelEditorEntryViewModel> ThrottleFixedLevels { get; }

    public IReadOnlyList<FixedLevelEditorEntryViewModel> BrakeFixedLevels { get; }

    public string ThrottleCutBinding
    {
        get => _throttleCutBinding;
        set => SetProperty(ref _throttleCutBinding, value);
    }

    public string EmergencyDisableBinding
    {
        get => _emergencyDisableBinding;
        set => SetProperty(ref _emergencyDisableBinding, value);
    }

    public string RatchetIncreaseBinding
    {
        get => _ratchetIncreaseBinding;
        set => SetProperty(ref _ratchetIncreaseBinding, value);
    }

    public string RatchetDecreaseBinding
    {
        get => _ratchetDecreaseBinding;
        set => SetProperty(ref _ratchetDecreaseBinding, value);
    }

    public string RatchetResetBinding
    {
        get => _ratchetResetBinding;
        set => SetProperty(ref _ratchetResetBinding, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!TryCreateFixedLevels(ThrottleFixedLevels, "Throttle", out var throttleFixedLevels, out var validationMessage) ||
            !TryCreateFixedLevels(BrakeFixedLevels, "Brake", out var brakeFixedLevels, out validationMessage))
        {
            ValidationMessage = validationMessage;
            return;
        }

        var candidate = _sourceConfiguration with
        {
            Input = _sourceConfiguration.Input with
            {
                ThrottleCutBinding = ThrottleCutBinding,
                EmergencyDisableBinding = EmergencyDisableBinding
            },
            Throttle = _sourceConfiguration.Throttle with
            {
                PrimaryBinding = ThrottlePrimaryBinding,
                FixedLevels = throttleFixedLevels
            },
            Brake = _sourceConfiguration.Brake with
            {
                PrimaryBinding = BrakePrimaryBinding,
                FixedLevels = brakeFixedLevels
            },
            Ratchet = _sourceConfiguration.Ratchet with
            {
                IncreaseBinding = RatchetIncreaseBinding,
                DecreaseBinding = RatchetDecreaseBinding,
                ResetBinding = RatchetResetBinding
            }
        };

        var errors = ConfigurationValidator.Validate(candidate);
        if (errors.Count != 0)
        {
            ValidationMessage = string.Join(Environment.NewLine, errors.Select(error => error.Message));
            return;
        }

        await _configurationService.SaveAsync(candidate, cancellationToken);
        ValidationMessage = string.Empty;
    }

    private static IReadOnlyList<FixedLevelEditorEntryViewModel> CreateEntries(IReadOnlyDictionary<string, double> fixedLevels) =>
        fixedLevels.Select(fixedLevel => new FixedLevelEditorEntryViewModel(fixedLevel.Key, fixedLevel.Value)).ToArray();

    private static string GetBindingAtLevel(IReadOnlyList<FixedLevelEditorEntryViewModel> fixedLevels, double level) =>
        fixedLevels.FirstOrDefault(fixedLevel => fixedLevel.Level == level)?.Binding ?? string.Empty;

    private void SetBindingAtLevel(
        IReadOnlyList<FixedLevelEditorEntryViewModel> fixedLevels,
        double level,
        string binding,
        string propertyName)
    {
        var fixedLevel = fixedLevels.FirstOrDefault(entry => entry.Level == level);
        if (fixedLevel is null || fixedLevel.Binding == binding)
        {
            return;
        }

        fixedLevel.Binding = binding;
        OnPropertyChanged(propertyName);
    }

    private static bool TryCreateFixedLevels(
        IReadOnlyList<FixedLevelEditorEntryViewModel> entries,
        string channelName,
        out IReadOnlyDictionary<string, double> fixedLevels,
        out string validationMessage)
    {
        var duplicate = entries
            .GroupBy(entry => entry.Binding, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            fixedLevels = new Dictionary<string, double>();
            validationMessage = $"{channelName} fixed bindings contain duplicate entries: '{duplicate.Key}'.";
            return false;
        }

        fixedLevels = entries.ToDictionary(entry => entry.Binding, entry => entry.Level, StringComparer.OrdinalIgnoreCase);
        validationMessage = string.Empty;
        return true;
    }
}

/// <summary>
/// An editable fixed-level binding retained from the configuration snapshot.
/// </summary>
public sealed class FixedLevelEditorEntryViewModel : ObservableObject
{
    private string _binding;

    public FixedLevelEditorEntryViewModel(string binding, double level)
    {
        _binding = binding;
        Level = level;
    }

    public string Binding
    {
        get => _binding;
        set => SetProperty(ref _binding, value);
    }

    public double Level { get; }
}
