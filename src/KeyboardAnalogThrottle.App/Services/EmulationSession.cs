using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Emulation;

namespace KeyboardAnalogThrottle.App.Services;

/// <summary>
/// Lazily owns emulation resources and serializes them with the controller diagnostic sequence.
/// </summary>
public interface IEmulationSession : IAsyncDisposable
{
    EmulationState State { get; }

    event EventHandler<EmulationState>? StateChanged;

    event EventHandler<ControllerTestProgress>? ControllerTestProgressChanged;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task EmergencyResetAsync(CancellationToken cancellationToken);

    Task RunControllerTestAsync(CancellationToken cancellationToken);
}

public sealed class EmulationSession : IEmulationSession
{
    private readonly Func<IEmulationEngine> _createEngine;
    private readonly IControllerTestService _controllerTest;
    private readonly Action<bool>? _setInputRunning;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _testGate = new();
    private IEmulationEngine? _engine;
    private CancellationTokenSource? _controllerTestCancellation;
    private EmulationState _state = EmulationState.Stopped;
    private int _disposed;

    public EmulationSession(
        Func<IEmulationEngine> createEngine,
        IControllerTestService controllerTest,
        Action<bool>? setInputRunning = null)
    {
        _createEngine = createEngine ?? throw new ArgumentNullException(nameof(createEngine));
        _controllerTest = controllerTest ?? throw new ArgumentNullException(nameof(controllerTest));
        _setInputRunning = setInputRunning;
        _controllerTest.ProgressChanged += OnControllerTestProgressChanged;
    }

    public EmulationState State => Volatile.Read(ref _state);

    public event EventHandler<EmulationState>? StateChanged;

    public event EventHandler<ControllerTestProgress>? ControllerTestProgressChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await GetOrCreateEngine().StartAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_engine is not null)
            {
                await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task EmergencyResetAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        CancelControllerTest();
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _setInputRunning?.Invoke(false);
            if (_engine is not null)
            {
                await _engine.EmergencyResetAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task RunControllerTestAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var testCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_testGate)
        {
            if (_controllerTestCancellation is not null)
            {
                testCancellation.Dispose();
                throw new InvalidOperationException("A controller test is already pending or running.");
            }

            ThrowIfDisposed();
            _controllerTestCancellation = testCancellation;
        }

        return RunControllerTestCoreAsync(testCancellation);
    }

    private async Task RunControllerTestCoreAsync(CancellationTokenSource testCancellation)
    {
        var enteredOperationGate = false;
        try
        {
            await _operationGate.WaitAsync(testCancellation.Token).ConfigureAwait(false);
            enteredOperationGate = true;
            ThrowIfDisposed();
            if (_engine?.State.IsRunning == true)
            {
                await _engine.StopAsync(testCancellation.Token).ConfigureAwait(false);
            }

            testCancellation.Token.ThrowIfCancellationRequested();
            await _controllerTest.RunAsync(testCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            if (enteredOperationGate)
            {
                _operationGate.Release();
            }

            lock (_testGate)
            {
                if (ReferenceEquals(_controllerTestCancellation, testCancellation))
                {
                    _controllerTestCancellation = null;
                }
            }

            testCancellation.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CancelControllerTest();
        await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _setInputRunning?.Invoke(false);
            _controllerTest.ProgressChanged -= OnControllerTestProgressChanged;
            var engine = _engine;
            _engine = null;
            if (engine is not null)
            {
                engine.StateChanged -= OnEngineStateChanged;
                await engine.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private IEmulationEngine GetOrCreateEngine()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        var engine = _createEngine();
        engine.StateChanged += OnEngineStateChanged;
        _engine = engine;
        PublishState(engine.State);
        return engine;
    }

    private void CancelControllerTest()
    {
        lock (_testGate)
        {
            _controllerTestCancellation?.Cancel();
        }
    }

    private void OnEngineStateChanged(object? sender, EmulationState state) => PublishState(state);

    private void PublishState(EmulationState state)
    {
        Volatile.Write(ref _state, state);
        _setInputRunning?.Invoke(state.IsRunning);
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
                // Display subscribers cannot interfere with the emulation session.
            }
        }
    }

    private void OnControllerTestProgressChanged(object? sender, ControllerTestProgress progress)
    {
        var handlers = ControllerTestProgressChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<ControllerTestProgress> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, progress);
            }
            catch
            {
                // Test progress is informational only.
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
