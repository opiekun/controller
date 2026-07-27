using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Bindings;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Curves;
using KeyboardAnalogThrottle.Core.Input;
using KeyboardAnalogThrottle.Core.Output;
using KeyboardAnalogThrottle.Core.Strategies;

namespace KeyboardAnalogThrottle.Core.Emulation;

/// <summary>
/// Coordinates one keyboard input source and one virtual controller on a monotonic, cancellable loop.
/// </summary>
public sealed class EmulationEngine : IEmulationEngine
{
    private static readonly TimeSpan MaximumUiUpdateInterval = TimeSpan.FromSeconds(1d / 30d);

    private readonly AppConfiguration _configuration;
    private readonly IVirtualController _controller;
    private readonly IKeyboardInputSource _input;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly IInputStrategy _throttleStrategy;
    private readonly IInputStrategy _brakeStrategy;
    private readonly ThrottleCutResolver _throttleCut = new();
    private readonly InputBinding _throttleBinding;
    private readonly InputBinding _brakeBinding;
    private readonly InputBinding _cutBinding;
    private readonly InputBinding _emergencyBinding;
    private readonly IReadOnlyDictionary<InputBinding, double> _throttleFixedLevels;
    private readonly IReadOnlyDictionary<InputBinding, double> _brakeFixedLevels;
    private readonly CurveKind _throttleCurve;
    private readonly CurveKind _brakeCurve;
    private readonly ConflictMode _conflictMode;
    private readonly bool _simultaneousInputEnabled;
    private readonly bool _toggleThrottleCut;

    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private bool _resourcesStarted;
    private bool _stopping;
    private bool _disposed;
    private double _rawThrottle;
    private double _rawBrake;
    private (byte Right, byte Left)? _lastReport;
    private TimeSpan? _unhealthySince;
    private TimeSpan? _lastUiPublication;
    private EmulationState _state = EmulationState.Stopped;

    public EmulationEngine(
        AppConfiguration configuration,
        IVirtualController controller,
        IKeyboardInputSource input,
        IClock clock,
        ConflictMode conflictMode = ConflictMode.BrakeWins,
        bool simultaneousInputEnabled = false,
        bool toggleThrottleCut = false)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _conflictMode = conflictMode;
        _simultaneousInputEnabled = simultaneousInputEnabled;
        _toggleThrottleCut = toggleThrottleCut;

        _throttleBinding = BindingParser.Parse(configuration.Throttle.PrimaryBinding);
        _brakeBinding = BindingParser.Parse(configuration.Brake.PrimaryBinding);
        _cutBinding = BindingParser.Parse(configuration.Input.ThrottleCutBinding);
        _emergencyBinding = BindingParser.Parse(configuration.Input.EmergencyDisableBinding);
        _throttleFixedLevels = ParseFixedLevels(configuration.Throttle.FixedLevels);
        _brakeFixedLevels = ParseFixedLevels(configuration.Brake.FixedLevels);
        _throttleCurve = ParseCurve(configuration.Throttle.Curve);
        _brakeCurve = ParseCurve(configuration.Brake.Curve);
        _throttleStrategy = CreateStrategy(configuration.Throttle, configuration.Ratchet);
        _brakeStrategy = CreateStrategy(configuration.Brake, configuration.Ratchet);
    }

    public EmulationState State => Volatile.Read(ref _state);

    public event EventHandler<EmulationState>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_resourcesStarted || _stopping)
            {
                return;
            }

            var inputStarted = false;
            try
            {
                inputStarted = true;
                await _input.StartAsync(cancellationToken).ConfigureAwait(false);
                await _controller.ConnectAsync(cancellationToken).ConfigureAwait(false);

                if (!_controller.IsConnected || _controller.IsDisposed)
                {
                    throw new InvalidOperationException("The virtual controller did not enter a connected state.");
                }

                _resourcesStarted = true;
                _rawThrottle = 0d;
                _rawBrake = 0d;
                _lastReport = null;
                _unhealthySince = null;
                _throttleCut.Reset();
                _loopCancellation = new CancellationTokenSource();
                Publish(new EmulationState(true, 0d, 0d, 0d, 0d, 0, 0, ReadInputHealth(), null, true, true), force: true);
                _loopTask = Task.Run(() => RunLoopAsync(_loopCancellation.Token));
            }
            catch (Exception exception)
            {
                await CleanupStartedResourcesAsync(inputStarted).ConfigureAwait(false);
                Publish(StoppedWithFault(CreateFault(FaultKindFor(exception), exception)), force: true);
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? loop;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_resourcesStarted)
            {
                return;
            }

            _stopping = true;
            _loopCancellation?.Cancel();
            loop = _loopTask;
        }
        finally
        {
            _lifecycle.Release();
        }

        if (loop is not null)
        {
            await loop.ConfigureAwait(false);
        }

        await _lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_resourcesStarted)
            {
                await CleanupStartedResourcesAsync(inputStarted: true).ConfigureAwait(false);
                Publish(StoppedWithFault(null), force: true);
            }
        }
        finally
        {
            _stopping = false;
            _lifecycle.Release();
        }
    }

    public Task EmergencyResetAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
        await _input.DisposeAsync().ConfigureAwait(false);
        await _controller.DisposeAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var previousTimestamp = _clock.GetTimestamp();
            var frameInterval = TimeSpan.FromSeconds(1d / _configuration.Controller.UpdateRateHz);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var timestamp = _clock.GetTimestamp();
                var elapsed = ClampElapsed(timestamp - previousTimestamp);
                previousTimestamp = timestamp;
                ProcessFrame(timestamp, elapsed);
                await _clock.DelayAsync(frameInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A serialized StopAsync owns cleanup after normal cancellation.
        }
        catch (Exception exception)
        {
            await StopAfterFaultAsync(exception).ConfigureAwait(false);
        }
    }

    private void ProcessFrame(TimeSpan timestamp, TimeSpan elapsed)
    {
        var health = ReadInputHealth();
        if (health != InputHealth.Healthy)
        {
            _unhealthySince ??= timestamp;
            if (timestamp - _unhealthySince >= TimeSpan.FromMilliseconds(_configuration.Controller.InputLossTimeoutMilliseconds))
            {
                throw new InputHealthException(health);
            }

            Publish(State with { InputHealth = health }, force: false);
            return;
        }

        _unhealthySince = null;
        var snapshot = ReadInputSnapshot();
        if (_emergencyBinding.Matches(snapshot))
        {
            _loopCancellation?.Cancel();
            _ = Task.Run(() => EmergencyResetAsync(CancellationToken.None));
            return;
        }

        _rawThrottle = _throttleStrategy.Update(snapshot, _rawThrottle, elapsed, _configuration.Throttle);
        _rawBrake = _brakeStrategy.Update(snapshot, _rawBrake, elapsed, _configuration.Brake);

        var fixedThrottle = ResolveFixedOutput(snapshot, _throttleBinding, _throttleFixedLevels, _rawThrottle, _configuration.Throttle);
        var fixedBrake = ResolveFixedOutput(snapshot, _brakeBinding, _brakeFixedLevels, _rawBrake, _configuration.Brake);
        var throttle = _throttleCut.Resolve(snapshot, _cutBinding, fixedThrottle, _toggleThrottleCut);
        var resolved = ConflictResolver.Resolve(
            snapshot,
            throttle,
            fixedBrake,
            _throttleBinding.Primary,
            _brakeBinding.Primary,
            _conflictMode,
            _simultaneousInputEnabled);
        throttle = OutputCurve.Apply(resolved.Throttle, _throttleCurve, _configuration.Throttle.CustomExponent);
        var brake = OutputCurve.Apply(resolved.Brake, _brakeCurve, _configuration.Brake.CustomExponent);
        var right = TriggerConverter.ToByte(throttle);
        var left = TriggerConverter.ToByte(brake);

        ThrowIfControllerUnavailable();
        SubmitChangedReport(right, left);
        Publish(new EmulationState(true, _rawThrottle, _rawBrake, throttle, brake, right, left, health, null, true, true), force: false);
    }

    private async Task StopAfterFaultAsync(Exception exception)
    {
        await _lifecycle.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!_resourcesStarted)
            {
                return;
            }

            _stopping = true;
            _loopCancellation?.Cancel();
            var kind = FaultKindFor(exception);
            await CleanupStartedResourcesAsync(inputStarted: true).ConfigureAwait(false);
            Publish(StoppedWithFault(CreateFault(kind, exception)), force: true);
        }
        finally
        {
            _stopping = false;
            _lifecycle.Release();
        }
    }

    private async Task CleanupStartedResourcesAsync(bool inputStarted)
    {
        _loopCancellation?.Cancel();
        _loopCancellation?.Dispose();
        _loopCancellation = null;
        _loopTask = null;
        SafeZeroReport();

        try
        {
            if (_controller.IsConnected && !_controller.IsDisposed)
            {
                await _controller.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // The remaining cleanup steps are safety-critical even if the driver fails to disconnect.
        }
        finally
        {
            if (inputStarted)
            {
                try
                {
                    await _input.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort only: the hook implementation must also make StopAsync idempotent.
                }
            }

            _resourcesStarted = false;
            _lastReport = null;
            _rawThrottle = 0d;
            _rawBrake = 0d;
            _unhealthySince = null;
            _throttleCut.Reset();
        }
    }

    private void SubmitChangedReport(byte right, byte left)
    {
        if (_lastReport is { } previous && previous.Right == right && previous.Left == left)
        {
            return;
        }

        ThrowIfControllerUnavailable();
        _controller.SetRightTrigger(right);
        ThrowIfControllerUnavailable();
        _controller.SetLeftTrigger(left);
        ThrowIfControllerUnavailable();
        _controller.SubmitReport();
        _lastReport = (right, left);
    }

    private void SafeZeroReport()
    {
        if (!_controller.IsConnected || _controller.IsDisposed)
        {
            return;
        }

        try
        {
            _controller.SetRightTrigger(0);
        }
        catch
        {
        }

        if (_controller.IsDisposed || !_controller.IsConnected)
        {
            return;
        }

        try
        {
            _controller.SetLeftTrigger(0);
        }
        catch
        {
        }

        if (_controller.IsDisposed || !_controller.IsConnected)
        {
            return;
        }

        try
        {
            _controller.SubmitReport();
        }
        catch
        {
        }
    }

    private void ThrowIfControllerUnavailable()
    {
        if (_controller.IsDisposed || !_controller.IsConnected)
        {
            throw new InvalidOperationException("The virtual controller is unavailable.");
        }
    }

    private void Publish(EmulationState state, bool force)
    {
        var timestamp = _clock.GetTimestamp();
        if (!force && _lastUiPublication is { } previous && timestamp - previous < MaximumUiUpdateInterval)
        {
            Volatile.Write(ref _state, state);
            return;
        }

        _lastUiPublication = timestamp;
        Volatile.Write(ref _state, state);
        var handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<EmulationState> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, state);
            }
            catch
            {
                // UI event subscribers cannot be allowed to terminate the safety loop.
            }
        }
    }

    private EmulationState StoppedWithFault(EmulationFault? fault) => new(
        false, 0d, 0d, 0d, 0d, 0, 0, ReadInputHealthForState(), fault, false, false);

    private InputHealth ReadInputHealth()
    {
        try
        {
            return _input.Health;
        }
        catch (Exception exception)
        {
            throw new InputSourceException("Keyboard input health could not be read.", exception);
        }
    }

    private InputHealth ReadInputHealthForState()
    {
        try
        {
            return _input.Health;
        }
        catch
        {
            return InputHealth.Unavailable;
        }
    }

    private InputSnapshot ReadInputSnapshot()
    {
        try
        {
            return _input.GetSnapshot();
        }
        catch (Exception exception)
        {
            throw new InputSourceException("Keyboard input snapshot could not be read.", exception);
        }
    }

    private TimeSpan ClampElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var maximum = TimeSpan.FromMilliseconds(_configuration.Controller.MaximumFrameDeltaMilliseconds);
        return elapsed > maximum ? maximum : elapsed;
    }

    private static IInputStrategy CreateStrategy(ChannelConfiguration channel, RatchetConfiguration ratchet) => channel.Mode switch
    {
        InputMode.Ramp => new RampInputStrategy(),
        InputMode.Fixed => new FixedInputStrategy(),
        InputMode.Ratchet => new RatchetInputStrategy(ratchet),
        _ => throw new ArgumentOutOfRangeException(nameof(channel.Mode), channel.Mode, "Unsupported input mode.")
    };

    private static CurveKind ParseCurve(string curve) =>
        Enum.TryParse<CurveKind>(curve, ignoreCase: true, out var result) && Enum.IsDefined(result)
            ? result
            : CurveKind.Linear;

    private static IReadOnlyDictionary<InputBinding, double> ParseFixedLevels(IReadOnlyDictionary<string, double> levels) =>
        levels.ToDictionary(static entry => BindingParser.Parse(entry.Key), static entry => entry.Value);

    private static double ResolveFixedOutput(
        InputSnapshot snapshot,
        InputBinding primaryBinding,
        IReadOnlyDictionary<InputBinding, double> fixedLevels,
        double rawValue,
        ChannelConfiguration channel)
    {
        if (!primaryBinding.Matches(snapshot))
        {
            return rawValue;
        }

        var fixedLevel = FixedBindingResolver.Resolve(snapshot, fixedLevels);
        if (fixedLevel is null)
        {
            return rawValue;
        }

        var maximum = double.IsFinite(channel.MaximumLevel) ? Math.Clamp(channel.MaximumLevel, 0d, 1d) : 0d;
        var normalized = double.IsFinite(fixedLevel.Value) ? Math.Clamp(fixedLevel.Value, 0d, 1d) : 0d;
        return Math.Min(normalized, maximum);
    }

    private static EmulationFault CreateFault(EmulationFaultKind kind, Exception exception) =>
        new(kind, exception.Message, exception);

    private static EmulationFaultKind FaultKindFor(Exception exception) => exception switch
    {
        InputHealthException inputHealthException => inputHealthException.Health == InputHealth.Synchronizing
            ? EmulationFaultKind.InputSynchronizationTimedOut
            : EmulationFaultKind.InputUnavailable,
        InputSourceException => EmulationFaultKind.InputUnavailable,
        ObjectDisposedException or InvalidOperationException => EmulationFaultKind.Controller,
        _ => EmulationFaultKind.Unexpected
    };

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EmulationEngine));
        }
    }

    private sealed class InputHealthException(InputHealth health) : Exception($"Keyboard input is {health}.")
    {
        public InputHealth Health { get; } = health;
    }

    private sealed class InputSourceException(string message, Exception innerException) : Exception(message, innerException);
}
