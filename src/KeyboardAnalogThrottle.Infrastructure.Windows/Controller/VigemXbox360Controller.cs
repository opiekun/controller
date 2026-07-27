using KeyboardAnalogThrottle.Core.Abstractions;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Controller;

/// <summary>
/// Keeps Nefarius ViGEm details behind the platform-neutral virtual-controller contract.
/// </summary>
public sealed class VigemXbox360Controller : IVirtualController
{
    private readonly object _gate = new();
    private readonly ViGEmClient _client;
    private readonly IXbox360Controller _target;
    private bool _connected;
    private bool _disposed;

    public VigemXbox360Controller()
    {
        ViGEmClient? client = null;
        try
        {
            client = new ViGEmClient();
            var target = client.CreateXbox360Controller();
            target.AutoSubmitReport = false;
            _client = client;
            _target = target;
        }
        catch (Exception exception) when (IsDriverUnavailable(exception))
        {
            client?.Dispose();
            throw new VigemDriverException(exception);
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _connected;
            }
        }
    }

    public bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_connected)
            {
                ResetReportNoLock();
                return Task.CompletedTask;
            }

            try
            {
                _target.Connect();
                _connected = true;
                ResetReportNoLock();
            }
            catch (Exception exception) when (IsDriverUnavailable(exception))
            {
                DisconnectAfterFailedConnectNoLock();
                throw new VigemDriverException(exception);
            }
            catch
            {
                DisconnectAfterFailedConnectNoLock();
                throw;
            }

            return Task.CompletedTask;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_connected)
            {
                return Task.CompletedTask;
            }

            DisconnectNoLock();
            return Task.CompletedTask;
        }
    }

    public void SetRightTrigger(byte value)
    {
        lock (_gate)
        {
            ThrowIfWritable();
            _target.SetSliderValue(Xbox360Slider.RightTrigger, value);
        }
    }

    public void SetLeftTrigger(byte value)
    {
        lock (_gate)
        {
            ThrowIfWritable();
            _target.SetSliderValue(Xbox360Slider.LeftTrigger, value);
        }
    }

    public void SubmitReport()
    {
        lock (_gate)
        {
            ThrowIfWritable();
            _target.SubmitReport();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            // Mark disposed before touching the target so no caller can issue another report once this lock releases.
            _disposed = true;
            try
            {
                if (_connected)
                {
                    try
                    {
                        ResetReportNoLock();
                    }
                    catch
                    {
                        // Disposal is best-effort, but subsequent writes remain permanently blocked.
                    }

                    try
                    {
                        _target.Disconnect();
                    }
                    catch
                    {
                        // The target and client still need disposal even when the bus rejects disconnect.
                    }
                    finally
                    {
                        _connected = false;
                    }
                }
            }
            finally
            {
                try
                {
                    ((IDisposable)_target).Dispose();
                }
                finally
                {
                    _client.Dispose();
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private void DisconnectNoLock()
    {
        try
        {
            ResetReportNoLock();
        }
        finally
        {
            // Change state before calling the driver to ensure a failed disconnect cannot permit a later write.
            _connected = false;
            _target.Disconnect();
        }
    }

    private void DisconnectAfterFailedConnectNoLock()
    {
        if (!_connected)
        {
            return;
        }

        _connected = false;
        try
        {
            _target.Disconnect();
        }
        catch
        {
            // The original connection failure remains the actionable error.
        }
    }

    private void ResetReportNoLock()
    {
        _target.ResetReport();
        _target.SubmitReport();
    }

    private void ThrowIfWritable()
    {
        ThrowIfDisposed();
        if (!_connected)
        {
            throw new InvalidOperationException("The virtual controller is not connected.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(VigemXbox360Controller));
        }
    }

    private static bool IsDriverUnavailable(Exception exception) =>
        exception is VigemBusNotFoundException || IsInitializationFailure(exception);

    private static bool IsInitializationFailure(Exception exception) => exception is
        DllNotFoundException or
        BadImageFormatException or
        TypeInitializationException or
        FileLoadException or
        VigemAllocFailedException;
}
