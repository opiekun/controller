using KeyboardAnalogThrottle.App.ViewModels;
using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;

namespace KeyboardAnalogThrottle.App.Tests.ViewModels;

public sealed class ShortcutEditorViewModelTests
{
    [Fact]
    public void Loads_all_editable_shortcut_bindings_from_current_configuration()
    {
        var configuration = CreateConfiguration(
            throttleFixedLevels: new Dictionary<string, double>
            {
                ["Ctrl+T"] = .25d,
                ["Alt+T"] = .5d,
                ["Shift+T"] = .75d,
                ["Ctrl+Shift+T"] = 1d
            },
            brakeFixedLevels: new Dictionary<string, double>
            {
                ["Ctrl+B"] = .25d,
                ["Alt+B"] = .5d,
                ["Shift+B"] = .75d,
                ["Ctrl+Shift+B"] = 1d
            });

        var editor = new ShortcutEditorViewModel(new RecordingConfigurationService(configuration));

        Assert.Equal("T", editor.ThrottlePrimaryBinding);
        Assert.Equal("Ctrl+T", editor.ThrottleFixed25Binding);
        Assert.Equal("Alt+T", editor.ThrottleFixed50Binding);
        Assert.Equal("Shift+T", editor.ThrottleFixed75Binding);
        Assert.Equal("Ctrl+Shift+T", editor.ThrottleFixed100Binding);
        Assert.Equal("B", editor.BrakePrimaryBinding);
        Assert.Equal("Ctrl+B", editor.BrakeFixed25Binding);
        Assert.Equal("Alt+B", editor.BrakeFixed50Binding);
        Assert.Equal("Shift+B", editor.BrakeFixed75Binding);
        Assert.Equal("Ctrl+Shift+B", editor.BrakeFixed100Binding);
        Assert.Equal("X", editor.ThrottleCutBinding);
        Assert.Equal("Ctrl+Alt+F11", editor.EmergencyDisableBinding);
        Assert.Equal("U", editor.RatchetIncreaseBinding);
        Assert.Equal("J", editor.RatchetDecreaseBinding);
        Assert.Equal("K", editor.RatchetResetBinding);
        Assert.Empty(editor.ValidationMessage);
    }

    [Fact]
    public async Task Save_builds_new_configuration_with_all_edited_bindings_and_preserves_fixed_levels()
    {
        var configuration = CreateConfiguration(
            throttleFixedLevels: new Dictionary<string, double>
            {
                ["Ctrl+T"] = .25d,
                ["Alt+T"] = .5d,
                ["Shift+T"] = .75d,
                ["Ctrl+Shift+T"] = 1d
            },
            brakeFixedLevels: new Dictionary<string, double>
            {
                ["Ctrl+B"] = .25d,
                ["Alt+B"] = .5d,
                ["Shift+B"] = .75d,
                ["Ctrl+Shift+B"] = 1d
            });
        var service = new RecordingConfigurationService(configuration);
        var editor = new ShortcutEditorViewModel(service)
        {
            ThrottlePrimaryBinding = "Y",
            ThrottleFixed25Binding = "Ctrl+Y",
            ThrottleFixed50Binding = "Alt+Y",
            ThrottleFixed75Binding = "Shift+Y",
            ThrottleFixed100Binding = "Ctrl+Shift+Y",
            BrakePrimaryBinding = "H",
            BrakeFixed25Binding = "Ctrl+H",
            BrakeFixed50Binding = "Alt+H",
            BrakeFixed75Binding = "Shift+H",
            BrakeFixed100Binding = "Ctrl+Shift+H",
            ThrottleCutBinding = "Z",
            EmergencyDisableBinding = "Ctrl+Alt+F10",
            RatchetIncreaseBinding = "I",
            RatchetDecreaseBinding = "K",
            RatchetResetBinding = "L"
        };

        await editor.SaveAsync(CancellationToken.None);

        var saved = Assert.Single(service.SavedConfigurations);
        Assert.NotSame(configuration, saved);
        Assert.Equal("Y", saved.Throttle.PrimaryBinding);
        Assert.Equal(new Dictionary<string, double>
        {
            ["Ctrl+Y"] = .25d,
            ["Alt+Y"] = .5d,
            ["Shift+Y"] = .75d,
            ["Ctrl+Shift+Y"] = 1d
        }, saved.Throttle.FixedLevels);
        Assert.Equal("H", saved.Brake.PrimaryBinding);
        Assert.Equal(new Dictionary<string, double>
        {
            ["Ctrl+H"] = .25d,
            ["Alt+H"] = .5d,
            ["Shift+H"] = .75d,
            ["Ctrl+Shift+H"] = 1d
        }, saved.Brake.FixedLevels);
        Assert.Equal("Z", saved.Input.ThrottleCutBinding);
        Assert.Equal("Ctrl+Alt+F10", saved.Input.EmergencyDisableBinding);
        Assert.Equal("I", saved.Ratchet.IncreaseBinding);
        Assert.Equal("K", saved.Ratchet.DecreaseBinding);
        Assert.Equal("L", saved.Ratchet.ResetBinding);
        Assert.Equal("T", configuration.Throttle.PrimaryBinding);
        Assert.Equal("B", configuration.Brake.PrimaryBinding);
        Assert.Empty(editor.ValidationMessage);
    }

    [Fact]
    public async Task Save_rejects_invalid_binding_without_saving_or_discarding_field()
    {
        var service = new RecordingConfigurationService(CreateConfiguration());
        var editor = new ShortcutEditorViewModel(service) { ThrottlePrimaryBinding = "invalid binding" };

        await editor.SaveAsync(CancellationToken.None);

        Assert.Equal("invalid binding", editor.ThrottlePrimaryBinding);
        Assert.Equal("Binding 'invalid binding' is invalid.", editor.ValidationMessage);
        Assert.Empty(service.SavedConfigurations);
    }

    [Fact]
    public async Task Save_rejects_emergency_binding_that_conflicts_with_output_without_saving_or_discarding_field()
    {
        var service = new RecordingConfigurationService(CreateConfiguration());
        var editor = new ShortcutEditorViewModel(service) { EmergencyDisableBinding = "T" };

        await editor.SaveAsync(CancellationToken.None);

        Assert.Equal("T", editor.EmergencyDisableBinding);
        Assert.Equal(
            "Emergency disable binding 'T' conflicts with mapped output binding 'T' (Throttle.PrimaryBinding).",
            editor.ValidationMessage);
        Assert.Empty(service.SavedConfigurations);
    }

    [Fact]
    public async Task Save_preserves_every_nonstandard_fixed_level_entry_without_mutating_source_dictionary()
    {
        var throttleFixedLevels = new Dictionary<string, double>
        {
            ["Ctrl+T"] = .25d,
            ["F1"] = .35d,
            ["F2"] = .6d
        };
        var brakeFixedLevels = new Dictionary<string, double>
        {
            ["Ctrl+B"] = .25d,
            ["F3"] = .4d
        };
        var configuration = CreateConfiguration(throttleFixedLevels, brakeFixedLevels);
        var service = new RecordingConfigurationService(configuration);
        var editor = new ShortcutEditorViewModel(service);
        var throttleCustomEntry = Assert.Single(editor.ThrottleFixedLevels, entry => entry.Level == .35d);
        var brakeCustomEntry = Assert.Single(editor.BrakeFixedLevels, entry => entry.Level == .4d);

        throttleCustomEntry.Binding = "F4";
        brakeCustomEntry.Binding = "F5";
        await editor.SaveAsync(CancellationToken.None);

        var saved = Assert.Single(service.SavedConfigurations);
        Assert.Equal(new Dictionary<string, double>
        {
            ["Ctrl+T"] = .25d,
            ["F4"] = .35d,
            ["F2"] = .6d
        }, saved.Throttle.FixedLevels);
        Assert.Equal(new Dictionary<string, double>
        {
            ["Ctrl+B"] = .25d,
            ["F5"] = .4d
        }, saved.Brake.FixedLevels);
        Assert.Equal(new Dictionary<string, double>
        {
            ["Ctrl+T"] = .25d,
            ["F1"] = .35d,
            ["F2"] = .6d
        }, throttleFixedLevels);
        Assert.Equal(new Dictionary<string, double>
        {
            ["Ctrl+B"] = .25d,
            ["F3"] = .4d
        }, brakeFixedLevels);
    }

    [Fact]
    public async Task Save_rejects_duplicate_fixed_binding_strings_before_dictionary_creation()
    {
        var service = new RecordingConfigurationService(CreateConfiguration());
        var editor = new ShortcutEditorViewModel(service);
        editor.ThrottleFixedLevels[0].Binding = "F1";
        editor.ThrottleFixedLevels[1].Binding = "F1";

        await editor.SaveAsync(CancellationToken.None);

        Assert.Equal("Throttle fixed bindings contain duplicate entries: 'F1'.", editor.ValidationMessage);
        Assert.Empty(service.SavedConfigurations);
    }

    [Fact]
    public async Task Save_rebases_deliberate_edit_on_reloaded_configuration()
    {
        var initial = CreateConfiguration(
            brakeFixedLevels: new Dictionary<string, double>
            {
                ["Ctrl+B"] = .25d,
                ["Alt+B"] = .5d,
                ["Shift+B"] = .75d,
                ["Ctrl+Shift+B"] = 1d
            });
        var service = new RecordingConfigurationService(initial);
        var editor = new ShortcutEditorViewModel(service)
        {
            ThrottlePrimaryBinding = "Y"
        };
        var reloaded = initial with
        {
            Controller = initial.Controller with { UpdateRateHz = 60 },
            Brake = initial.Brake with
            {
                PrimaryBinding = "N",
                FixedLevels = new Dictionary<string, double>
                {
                    ["Ctrl+N"] = .25d,
                    ["Alt+N"] = .5d,
                    ["Shift+N"] = .75d,
                    ["Ctrl+Shift+N"] = 1d
                }
            }
        };

        await service.PublishReloadAsync(reloaded);

        Assert.Equal("Y", editor.ThrottlePrimaryBinding);
        Assert.Equal("N", editor.BrakePrimaryBinding);
        Assert.Equal("Ctrl+N", Assert.Single(editor.BrakeFixedLevels, entry => entry.Level == .25d).Binding);
        await editor.SaveAsync(CancellationToken.None);

        var saved = Assert.Single(service.SavedConfigurations);
        Assert.Equal("Y", saved.Throttle.PrimaryBinding);
        Assert.Equal(60, saved.Controller.UpdateRateHz);
        Assert.Equal("N", saved.Brake.PrimaryBinding);
        Assert.Equal(new Dictionary<string, double>
        {
            ["Ctrl+N"] = .25d,
            ["Alt+N"] = .5d,
            ["Shift+N"] = .75d,
            ["Ctrl+Shift+N"] = 1d
        }, saved.Brake.FixedLevels);
    }

    [Fact]
    public async Task Save_applies_deliberate_shortcut_edit_to_configuration_reloaded_while_waiting_for_update_gate()
    {
        var initial = CreateConfiguration();
        var reloaded = initial with
        {
            Controller = initial.Controller with { UpdateRateHz = 60 },
            Brake = initial.Brake with { PrimaryBinding = "N" }
        };
        var service = new RecordingConfigurationService(initial)
        {
            ConfigurationToApplyDuringUpdate = reloaded
        };
        var editor = new ShortcutEditorViewModel(service)
        {
            ThrottlePrimaryBinding = "Y"
        };

        await editor.SaveAsync(CancellationToken.None);

        var saved = Assert.Single(service.SavedConfigurations);
        Assert.Equal(60, saved.Controller.UpdateRateHz);
        Assert.Equal("N", saved.Brake.PrimaryBinding);
        Assert.Equal("Y", saved.Throttle.PrimaryBinding);
    }

    [Fact]
    public async Task Background_configuration_change_waits_for_editor_refresh_on_captured_synchronization_context()
    {
        var previousContext = SynchronizationContext.Current;
        var context = new RecordingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        ShortcutEditorViewModel? editor = null;
        try
        {
            var initial = CreateConfiguration();
            var service = new RecordingConfigurationService(initial);
            editor = new ShortcutEditorViewModel(service);
            SynchronizationContext.SetSynchronizationContext(previousContext);
            var reloaded = initial with { Brake = initial.Brake with { PrimaryBinding = "N" } };

            var reload = Task.Run(() => service.PublishReloadAsync(reloaded));
            await context.Posted.Task.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.False(reload.IsCompleted);
            context.RunPostedCallbacks();
            await reload;
            Assert.Equal("N", editor.BrakePrimaryBinding);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
            editor?.Dispose();
        }
    }

    private static AppConfiguration CreateConfiguration(
        IReadOnlyDictionary<string, double>? throttleFixedLevels = null,
        IReadOnlyDictionary<string, double>? brakeFixedLevels = null)
    {
        var defaults = AppConfiguration.CreateDefault();
        return defaults with
        {
            Input = defaults.Input with
            {
                ThrottleCutBinding = "X",
                EmergencyDisableBinding = "Ctrl+Alt+F11"
            },
            Throttle = defaults.Throttle with
            {
                PrimaryBinding = "T",
                FixedLevels = throttleFixedLevels ?? defaults.Throttle.FixedLevels
            },
            Brake = defaults.Brake with
            {
                PrimaryBinding = "B",
                FixedLevels = brakeFixedLevels ?? defaults.Brake.FixedLevels
            },
            Ratchet = defaults.Ratchet with
            {
                IncreaseBinding = "U",
                DecreaseBinding = "J",
                ResetBinding = "K"
            }
        };
    }

    private sealed class RecordingConfigurationService(AppConfiguration current) : IConfigurationService
    {
        public AppConfiguration Current { get; private set; } = current;

        public List<AppConfiguration> SavedConfigurations { get; } = [];

        public AppConfiguration? ConfigurationToApplyDuringUpdate { get; init; }

        public event Func<AppConfiguration, CancellationToken, Task>? ConfigurationChanged;

        public Task<ConfigurationReloadResult> ReloadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ConfigurationReloadResult.Success);

        public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken)
        {
            SavedConfigurations.Add(configuration);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Func<AppConfiguration, AppConfiguration> update, CancellationToken cancellationToken)
        {
            Current = ConfigurationToApplyDuringUpdate ?? Current;
            var configuration = update(Current);
            SavedConfigurations.Add(configuration);
            return Task.CompletedTask;
        }

        public async Task PublishReloadAsync(AppConfiguration configuration)
        {
            var handlers = ConfigurationChanged;
            if (handlers is not null)
            {
                foreach (Func<AppConfiguration, CancellationToken, Task> handler in handlers.GetInvocationList())
                {
                    await handler(configuration, CancellationToken.None);
                }
            }

            Current = configuration;
        }
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = [];

        public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _callbacks.Enqueue((callback, state));
            Posted.TrySetResult();
        }

        public void RunPostedCallbacks()
        {
            while (_callbacks.TryDequeue(out var callback))
            {
                callback.Callback(callback.State);
            }
        }
    }
}
