using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Core.Input;

namespace KeyboardAnalogThrottle.Core.Tests.Fakes;

public sealed class FakeKeyboardInputSource : IKeyboardInputSource
{
    private InputSnapshot _snapshot;
    private InputHealth _health = InputHealth.Healthy;

    public FakeKeyboardInputSource(InputSnapshot? snapshot = null)
    {
        _snapshot = snapshot ?? InputSnapshot.Empty;
    }

    public InputHealth Health
    {
        get
        {
            if (HealthException is not null)
            {
                throw HealthException;
            }

            return _health;
        }
        set => _health = value;
    }

    public bool IsStarted { get; private set; }

    public bool IsDisposed { get; private set; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public Exception? StartException { get; set; }

    public Exception? StopException { get; set; }

    public Exception? SnapshotException { get; set; }

    public Exception? HealthException { get; set; }

    public Action? OnStart { get; set; }

    public static FakeKeyboardInputSource Pressed(params InputKey[] keys) => new(InputSnapshot.FromPressed(keys));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        StartCount++;
        if (StartException is not null)
        {
            throw StartException;
        }

        IsStarted = true;
        OnStart?.Invoke();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopCount++;
        if (StopException is not null)
        {
            throw StopException;
        }

        IsStarted = false;
        return Task.CompletedTask;
    }

    public InputSnapshot GetSnapshot()
    {
        ThrowIfDisposed();
        if (SnapshotException is not null)
        {
            throw SnapshotException;
        }

        return _snapshot;
    }

    public void SetSnapshot(InputSnapshot snapshot) => _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        IsStarted = false;
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(FakeKeyboardInputSource));
        }
    }
}
