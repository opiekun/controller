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
    private readonly double _throttleFixed25Level;
    private readonly double _throttleFixed50Level;
    private readonly double _throttleFixed75Level;
    private readonly double _throttleFixed100Level;
    private readonly double _brakeFixed25Level;
    private readonly double _brakeFixed50Level;
    private readonly double _brakeFixed75Level;
    private readonly double _brakeFixed100Level;
    private string _throttlePrimaryBinding;
    private string _throttleFixed25Binding;
    private string _throttleFixed50Binding;
    private string _throttleFixed75Binding;
    private string _throttleFixed100Binding;
    private string _brakePrimaryBinding;
    private string _brakeFixed25Binding;
    private string _brakeFixed50Binding;
    private string _brakeFixed75Binding;
    private string _brakeFixed100Binding;
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

        var throttleDefaults = ChannelConfiguration.CreateThrottleDefault().FixedLevels;
        var brakeDefaults = ChannelConfiguration.CreateBrakeDefault().FixedLevels;
        (_throttleFixed25Binding, _throttleFixed25Level) = GetFixedLevel(_sourceConfiguration.Throttle.FixedLevels, .25d, throttleDefaults);
        (_throttleFixed50Binding, _throttleFixed50Level) = GetFixedLevel(_sourceConfiguration.Throttle.FixedLevels, .5d, throttleDefaults);
        (_throttleFixed75Binding, _throttleFixed75Level) = GetFixedLevel(_sourceConfiguration.Throttle.FixedLevels, .75d, throttleDefaults);
        (_throttleFixed100Binding, _throttleFixed100Level) = GetFixedLevel(_sourceConfiguration.Throttle.FixedLevels, 1d, throttleDefaults);
        (_brakeFixed25Binding, _brakeFixed25Level) = GetFixedLevel(_sourceConfiguration.Brake.FixedLevels, .25d, brakeDefaults);
        (_brakeFixed50Binding, _brakeFixed50Level) = GetFixedLevel(_sourceConfiguration.Brake.FixedLevels, .5d, brakeDefaults);
        (_brakeFixed75Binding, _brakeFixed75Level) = GetFixedLevel(_sourceConfiguration.Brake.FixedLevels, .75d, brakeDefaults);
        (_brakeFixed100Binding, _brakeFixed100Level) = GetFixedLevel(_sourceConfiguration.Brake.FixedLevels, 1d, brakeDefaults);

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
        get => _throttleFixed25Binding;
        set => SetProperty(ref _throttleFixed25Binding, value);
    }

    public string ThrottleFixed50Binding
    {
        get => _throttleFixed50Binding;
        set => SetProperty(ref _throttleFixed50Binding, value);
    }

    public string ThrottleFixed75Binding
    {
        get => _throttleFixed75Binding;
        set => SetProperty(ref _throttleFixed75Binding, value);
    }

    public string ThrottleFixed100Binding
    {
        get => _throttleFixed100Binding;
        set => SetProperty(ref _throttleFixed100Binding, value);
    }

    public string BrakePrimaryBinding
    {
        get => _brakePrimaryBinding;
        set => SetProperty(ref _brakePrimaryBinding, value);
    }

    public string BrakeFixed25Binding
    {
        get => _brakeFixed25Binding;
        set => SetProperty(ref _brakeFixed25Binding, value);
    }

    public string BrakeFixed50Binding
    {
        get => _brakeFixed50Binding;
        set => SetProperty(ref _brakeFixed50Binding, value);
    }

    public string BrakeFixed75Binding
    {
        get => _brakeFixed75Binding;
        set => SetProperty(ref _brakeFixed75Binding, value);
    }

    public string BrakeFixed100Binding
    {
        get => _brakeFixed100Binding;
        set => SetProperty(ref _brakeFixed100Binding, value);
    }

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
                FixedLevels = CreateFixedLevels(
                    ThrottleFixed25Binding,
                    _throttleFixed25Level,
                    ThrottleFixed50Binding,
                    _throttleFixed50Level,
                    ThrottleFixed75Binding,
                    _throttleFixed75Level,
                    ThrottleFixed100Binding,
                    _throttleFixed100Level)
            },
            Brake = _sourceConfiguration.Brake with
            {
                PrimaryBinding = BrakePrimaryBinding,
                FixedLevels = CreateFixedLevels(
                    BrakeFixed25Binding,
                    _brakeFixed25Level,
                    BrakeFixed50Binding,
                    _brakeFixed50Level,
                    BrakeFixed75Binding,
                    _brakeFixed75Level,
                    BrakeFixed100Binding,
                    _brakeFixed100Level)
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

    private static (string Binding, double Level) GetFixedLevel(
        IReadOnlyDictionary<string, double> fixedLevels,
        double level,
        IReadOnlyDictionary<string, double> defaults)
    {
        foreach (var fixedLevel in fixedLevels)
        {
            if (fixedLevel.Value == level)
            {
                return (fixedLevel.Key, fixedLevel.Value);
            }
        }

        var defaultBinding = defaults.Single(fixedLevel => fixedLevel.Value == level);
        return (defaultBinding.Key, defaultBinding.Value);
    }

    private static IReadOnlyDictionary<string, double> CreateFixedLevels(
        string binding25,
        double level25,
        string binding50,
        double level50,
        string binding75,
        double level75,
        string binding100,
        double level100) => new Dictionary<string, double>
    {
        [binding25] = level25,
        [binding50] = level50,
        [binding75] = level75,
        [binding100] = level100
    };
}
