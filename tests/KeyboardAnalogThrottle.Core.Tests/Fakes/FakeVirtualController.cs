using KeyboardAnalogThrottle.Core.Abstractions;

namespace KeyboardAnalogThrottle.Core.Tests.Fakes;

public sealed class FakeVirtualController : IVirtualController
{
    public bool IsConnected { get; private set; }

    public bool IsDisposed { get; private set; }

    public byte RightTrigger { get; private set; }

    public byte LeftTrigger { get; private set; }

    public int ConnectCount { get; private set; }

    public int DisconnectCount { get; private set; }

    public int SubmitCount { get; private set; }

    public int ZeroReportCount { get; private set; }

    public int SetRightCount { get; private set; }

    public int SetLeftCount { get; private set; }

    public Exception? ConnectException { get; set; }

    public Exception? DisconnectException { get; set; }

    public Exception? SetRightException { get; set; }

    public Exception? SetLeftException { get; set; }

    public Exception? SubmitException { get; set; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ConnectCount++;
        if (ConnectException is not null)
        {
            throw ConnectException;
        }

        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DisconnectCount++;
        if (DisconnectException is not null)
        {
            throw DisconnectException;
        }

        IsConnected = false;
        return Task.CompletedTask;
    }

    public void SetRightTrigger(byte value)
    {
        ThrowIfUnavailable();
        SetRightCount++;
        if (SetRightException is not null)
        {
            throw SetRightException;
        }

        RightTrigger = value;
    }

    public void SetLeftTrigger(byte value)
    {
        ThrowIfUnavailable();
        SetLeftCount++;
        if (SetLeftException is not null)
        {
            throw SetLeftException;
        }

        LeftTrigger = value;
    }

    public void SubmitReport()
    {
        ThrowIfUnavailable();
        SubmitCount++;
        if (SubmitException is not null)
        {
            throw SubmitException;
        }

        if (RightTrigger == 0 && LeftTrigger == 0)
        {
            ZeroReportCount++;
        }
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    public void ForceDisconnect() => IsConnected = false;

    private void ThrowIfUnavailable()
    {
        ThrowIfDisposed();
        if (!IsConnected)
        {
            throw new InvalidOperationException("Controller is not connected.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(FakeVirtualController));
        }
    }
}
