namespace KeyboardAnalogThrottle.Core.Abstractions;

/// <summary>
/// The platform-neutral surface of the virtual Xbox controller used by the engine.
/// </summary>
public interface IVirtualController : IAsyncDisposable
{
    bool IsConnected { get; }

    bool IsDisposed { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);

    void SetRightTrigger(byte value);

    void SetLeftTrigger(byte value);

    void SubmitReport();
}
