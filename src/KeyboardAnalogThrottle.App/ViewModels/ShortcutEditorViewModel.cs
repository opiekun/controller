using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using System.Collections.ObjectModel;

namespace KeyboardAnalogThrottle.App.ViewModels;

/// <summary>
/// Holds editable keyboard shortcut text separately from the active configuration snapshot.
/// </summary>
public sealed class ShortcutEditorViewModel : ObservableObject, IDisposable
{
    private readonly IConfigurationService _configurationService;
    private readonly ObservableCollection<FixedLevelEditorEntryViewModel> _throttleFixedLevels;
    private readonly ObservableCollection<FixedLevelEditorEntryViewModel> _brakeFixedLevels;
    private AppConfiguration _sourceConfiguration;
    private string _throttlePrimaryBinding;
    private string _brakePrimaryBinding;
    private string _throttleCutBinding;
    private string _emergencyDisableBinding;
    private string _ratchetIncreaseBinding;
    private string _ratchetDecreaseBinding;
    private string _ratchetResetBinding;
    private string _validationMessage = string.Empty;
    private bool _disposed;

    public ShortcutEditorViewModel(IConfigurationService configurationService)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _sourceConfiguration = configurationService.Current;

        _throttleFixedLevels = new ObservableCollection<FixedLevelEditorEntryViewModel>(CreateEntries(_sourceConfiguration.Throttle.FixedLevels));
        _brakeFixedLevels = new ObservableCollection<FixedLevelEditorEntryViewModel>(CreateEntries(_sourceConfiguration.Brake.FixedLevels));
        ThrottleFixedLevels = _throttleFixedLevels;
        BrakeFixedLevels = _brakeFixedLevels;
        _throttlePrimaryBinding = _sourceConfiguration.Throttle.PrimaryBinding;
        _brakePrimaryBinding = _sourceConfiguration.Brake.PrimaryBinding;
        _throttleCutBinding = _sourceConfiguration.Input.ThrottleCutBinding;
        _emergencyDisableBinding = _sourceConfiguration.Input.EmergencyDisableBinding;
        _ratchetIncreaseBinding = _sourceConfiguration.Ratchet.IncreaseBinding;
        _ratchetDecreaseBinding = _sourceConfiguration.Ratchet.DecreaseBinding;
        _ratchetResetBinding = _sourceConfiguration.Ratchet.ResetBinding;
        _configurationService.ConfigurationChanged += OnConfigurationChangedAsync;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _configurationService.ConfigurationChanged -= OnConfigurationChangedAsync;
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var candidate = CreateCandidate(_configurationService.Current, out var validationMessage);
        if (candidate is null)
        {
            ValidationMessage = validationMessage;
            return;
        }

        var errors = ConfigurationValidator.Validate(candidate);
        if (errors.Count != 0)
        {
            ValidationMessage = string.Join(Environment.NewLine, errors.Select(error => error.Message));
            return;
        }

        await _configurationService.SaveAsync(candidate, cancellationToken);
        RefreshFromConfiguration(candidate);
    }

    private Task OnConfigurationChangedAsync(AppConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!_disposed)
        {
            RefreshFromConfiguration(configuration);
        }

        return Task.CompletedTask;
    }

    private AppConfiguration? CreateCandidate(AppConfiguration latest, out string validationMessage)
    {
        var throttleFixedEntries = RebaseFixedLevels(
            ThrottleFixedLevels,
            _sourceConfiguration.Throttle.FixedLevels,
            latest.Throttle.FixedLevels);
        var brakeFixedEntries = RebaseFixedLevels(
            BrakeFixedLevels,
            _sourceConfiguration.Brake.FixedLevels,
            latest.Brake.FixedLevels);
        if (!TryCreateFixedLevels(throttleFixedEntries, "Throttle", out var throttleFixedLevels, out validationMessage) ||
            !TryCreateFixedLevels(brakeFixedEntries, "Brake", out var brakeFixedLevels, out validationMessage))
        {
            return null;
        }

        return latest with
        {
            Input = latest.Input with
            {
                ThrottleCutBinding = RebaseBinding(ThrottleCutBinding, _sourceConfiguration.Input.ThrottleCutBinding, latest.Input.ThrottleCutBinding),
                EmergencyDisableBinding = RebaseBinding(EmergencyDisableBinding, _sourceConfiguration.Input.EmergencyDisableBinding, latest.Input.EmergencyDisableBinding)
            },
            Throttle = latest.Throttle with
            {
                PrimaryBinding = RebaseBinding(ThrottlePrimaryBinding, _sourceConfiguration.Throttle.PrimaryBinding, latest.Throttle.PrimaryBinding),
                FixedLevels = throttleFixedLevels
            },
            Brake = latest.Brake with
            {
                PrimaryBinding = RebaseBinding(BrakePrimaryBinding, _sourceConfiguration.Brake.PrimaryBinding, latest.Brake.PrimaryBinding),
                FixedLevels = brakeFixedLevels
            },
            Ratchet = latest.Ratchet with
            {
                IncreaseBinding = RebaseBinding(RatchetIncreaseBinding, _sourceConfiguration.Ratchet.IncreaseBinding, latest.Ratchet.IncreaseBinding),
                DecreaseBinding = RebaseBinding(RatchetDecreaseBinding, _sourceConfiguration.Ratchet.DecreaseBinding, latest.Ratchet.DecreaseBinding),
                ResetBinding = RebaseBinding(RatchetResetBinding, _sourceConfiguration.Ratchet.ResetBinding, latest.Ratchet.ResetBinding)
            }
        };
    }

    private void RefreshFromConfiguration(AppConfiguration configuration)
    {
        ThrottlePrimaryBinding = RebaseBinding(ThrottlePrimaryBinding, _sourceConfiguration.Throttle.PrimaryBinding, configuration.Throttle.PrimaryBinding);
        BrakePrimaryBinding = RebaseBinding(BrakePrimaryBinding, _sourceConfiguration.Brake.PrimaryBinding, configuration.Brake.PrimaryBinding);
        ThrottleCutBinding = RebaseBinding(ThrottleCutBinding, _sourceConfiguration.Input.ThrottleCutBinding, configuration.Input.ThrottleCutBinding);
        EmergencyDisableBinding = RebaseBinding(EmergencyDisableBinding, _sourceConfiguration.Input.EmergencyDisableBinding, configuration.Input.EmergencyDisableBinding);
        RatchetIncreaseBinding = RebaseBinding(RatchetIncreaseBinding, _sourceConfiguration.Ratchet.IncreaseBinding, configuration.Ratchet.IncreaseBinding);
        RatchetDecreaseBinding = RebaseBinding(RatchetDecreaseBinding, _sourceConfiguration.Ratchet.DecreaseBinding, configuration.Ratchet.DecreaseBinding);
        RatchetResetBinding = RebaseBinding(RatchetResetBinding, _sourceConfiguration.Ratchet.ResetBinding, configuration.Ratchet.ResetBinding);
        ReplaceFixedLevels(
            _throttleFixedLevels,
            RebaseFixedLevels(ThrottleFixedLevels, _sourceConfiguration.Throttle.FixedLevels, configuration.Throttle.FixedLevels));
        ReplaceFixedLevels(
            _brakeFixedLevels,
            RebaseFixedLevels(BrakeFixedLevels, _sourceConfiguration.Brake.FixedLevels, configuration.Brake.FixedLevels));
        _sourceConfiguration = configuration;
        ValidationMessage = string.Empty;
        OnPropertyChanged(nameof(ThrottleFixed25Binding));
        OnPropertyChanged(nameof(ThrottleFixed50Binding));
        OnPropertyChanged(nameof(ThrottleFixed75Binding));
        OnPropertyChanged(nameof(ThrottleFixed100Binding));
        OnPropertyChanged(nameof(BrakeFixed25Binding));
        OnPropertyChanged(nameof(BrakeFixed50Binding));
        OnPropertyChanged(nameof(BrakeFixed75Binding));
        OnPropertyChanged(nameof(BrakeFixed100Binding));
    }

    private static IReadOnlyList<FixedLevelEditorEntryViewModel> CreateEntries(IReadOnlyDictionary<string, double> fixedLevels) =>
        fixedLevels.Select(fixedLevel => new FixedLevelEditorEntryViewModel(fixedLevel.Key, fixedLevel.Value)).ToArray();

    private static string RebaseBinding(string edited, string source, string latest) =>
        string.Equals(edited, source, StringComparison.Ordinal) ? latest : edited;

    private static IReadOnlyList<FixedLevelEditorEntryViewModel> RebaseFixedLevels(
        IReadOnlyList<FixedLevelEditorEntryViewModel> entries,
        IReadOnlyDictionary<string, double> sourceFixedLevels,
        IReadOnlyDictionary<string, double> latestFixedLevels)
    {
        var rebased = new List<FixedLevelEditorEntryViewModel>();
        foreach (var entry in entries)
        {
            var sourceBinding = sourceFixedLevels.FirstOrDefault(fixedLevel => fixedLevel.Value == entry.Level).Key;
            var latestBinding = latestFixedLevels.FirstOrDefault(fixedLevel => fixedLevel.Value == entry.Level).Key;
            var isEdited = sourceBinding is null || !string.Equals(entry.Binding, sourceBinding, StringComparison.Ordinal);
            if (latestBinding is not null)
            {
                rebased.Add(new FixedLevelEditorEntryViewModel(isEdited ? entry.Binding : latestBinding, entry.Level));
            }
            else if (isEdited)
            {
                rebased.Add(new FixedLevelEditorEntryViewModel(entry.Binding, entry.Level));
            }
        }

        foreach (var fixedLevel in latestFixedLevels)
        {
            if (!entries.Any(entry => entry.Level == fixedLevel.Value))
            {
                rebased.Add(new FixedLevelEditorEntryViewModel(fixedLevel.Key, fixedLevel.Value));
            }
        }

        return rebased;
    }

    private static void ReplaceFixedLevels(
        ObservableCollection<FixedLevelEditorEntryViewModel> destination,
        IReadOnlyList<FixedLevelEditorEntryViewModel> source)
    {
        destination.Clear();
        foreach (var entry in source)
        {
            destination.Add(entry);
        }
    }

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
