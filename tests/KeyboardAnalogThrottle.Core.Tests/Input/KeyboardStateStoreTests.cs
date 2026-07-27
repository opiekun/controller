using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Core.Input;
using KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

namespace KeyboardAnalogThrottle.Core.Tests.Input;

public sealed class KeyboardStateStoreTests
{
    [Fact]
    public void Repeated_keydown_does_not_create_a_second_down_transition()
    {
        var store = new KeyboardStateStore();

        store.ApplyDown(InputKey.W);
        store.ApplyDown(InputKey.W);

        var snapshot = store.GetSnapshot();
        Assert.True(snapshot.WasPressedThisFrame(InputKey.W));
        Assert.Equal(1, snapshot.TransitionSequence(InputKey.W));
        Assert.Single(snapshot.Transitions);
    }

    [Fact]
    public void Snapshot_returns_ordered_transitions_once_and_retains_pressed_state()
    {
        var store = new KeyboardStateStore();
        store.SetHealth(InputHealth.Healthy);
        store.ApplyDown(InputKey.W);
        store.ApplyUp(InputKey.W);

        var snapshot = store.GetSnapshot();

        Assert.Collection(
            snapshot.Transitions,
            transition => Assert.Equal(new KeyTransition(InputKey.W, true, 1), transition),
            transition => Assert.Equal(new KeyTransition(InputKey.W, false, 2), transition));
        Assert.False(snapshot.IsPressed(InputKey.W));
        Assert.Equal(2, snapshot.TransitionSequence(InputKey.W));
        Assert.Empty(store.GetSnapshot().Transitions);
    }

    [Fact]
    public void Callback_from_a_stopped_capture_generation_cannot_restore_keyboard_state()
    {
        var store = new KeyboardStateStore();
        var captureGeneration = store.BeginCapture();

        store.StopCapture();
        var notification = store.TryApplyDown(InputKey.W, captureGeneration);

        Assert.Null(notification);
        Assert.False(store.GetSnapshot().IsPressed(InputKey.W));
        Assert.Equal(InputHealth.Unavailable, store.Health);
    }
}
