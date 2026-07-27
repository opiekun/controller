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

    [Fact]
    public void Replaced_capture_identity_cannot_restore_or_consume_a_suppressed_key_bit()
    {
        var suppressedKeys = new CaptureSuppressedKeys();
        var firstCapture = suppressedKeys.BeginCapture(7);
        Assert.True(suppressedKeys.TryMark(InputKey.W, firstCapture));

        var secondCapture = suppressedKeys.BeginCapture(7);

        Assert.False(suppressedKeys.TryMark(InputKey.W, firstCapture));
        Assert.False(suppressedKeys.TryTake(InputKey.W, firstCapture));
        Assert.False(suppressedKeys.TryTake(InputKey.W, secondCapture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Suppression_decision_fails_open_when_capture_is_invalidated_after_its_state_change(bool take)
    {
        using var stateChanged = new ManualResetEventSlim();
        using var continueDecision = new ManualResetEventSlim();
        var pauseAfterStateChange = false;
        var suppressedKeys = new CaptureSuppressedKeys(() =>
        {
            if (!Volatile.Read(ref pauseAfterStateChange))
            {
                return;
            }

            stateChanged.Set();
            continueDecision.Wait();
        });
        var capture = suppressedKeys.BeginCapture(7);
        if (take)
        {
            Assert.True(suppressedKeys.TryMark(InputKey.W, capture));
        }

        Volatile.Write(ref pauseAfterStateChange, true);
        var decision = Task.Run(() => take
            ? suppressedKeys.TryTake(InputKey.W, capture)
            : suppressedKeys.TryMark(InputKey.W, capture));

        Assert.True(stateChanged.Wait(TimeSpan.FromSeconds(5)));
        suppressedKeys.EndCapture();
        continueDecision.Set();

        Assert.False(await decision);
    }
}
