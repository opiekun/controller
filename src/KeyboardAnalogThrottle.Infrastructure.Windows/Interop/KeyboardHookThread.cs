using System.Collections.Concurrent;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Interop;

internal interface IKeyboardHookThread : IAsyncDisposable
{
    Task InvokeAsync(Action operation);

    Task<T> InvokeAsync<T>(Func<T> operation);
}

/// <summary>
/// Owns the native message loop required by <c>WH_KEYBOARD_LL</c> and executes
/// lifecycle commands serially on that same long-lived thread.
/// </summary>
internal sealed class KeyboardHookThread : IKeyboardHookThread
{
    private readonly object _admissionGate = new();
    private readonly ConcurrentQueue<Command> _commands = [];
    private readonly IKeyboardHookMessageLoop _messageLoop;
    private readonly TaskCompletionSource<uint> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _terminated =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private ShutdownCommand? _shutdown;
    private bool _accepting = true;

    public KeyboardHookThread()
        : this(new Win32KeyboardHookMessageLoop())
    {
    }

    internal KeyboardHookThread(IKeyboardHookMessageLoop messageLoop)
    {
        ArgumentNullException.ThrowIfNull(messageLoop);
        _messageLoop = messageLoop;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Keyboard hook message loop"
        };
        _thread.Start();
    }

    internal bool IsAlive => _thread.IsAlive;

    public Task InvokeAsync(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return InvokeAsync(
            () =>
            {
                operation();
                return true;
            });
    }

    public async Task<T> InvokeAsync<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var threadId = await _ready.Task.ConfigureAwait(false);
        var command = new OperationCommand<T>(operation);

        lock (_admissionGate)
        {
            if (!_accepting)
            {
                throw new ObjectDisposedException(nameof(KeyboardHookThread));
            }

            _commands.Enqueue(command);
            try
            {
                _messageLoop.PostCommand(threadId);
            }
            catch (Exception exception)
            {
                command.Fail(exception);
                throw;
            }
        }

        return await command.Task.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        uint threadId;
        try
        {
            threadId = await _ready.Task.ConfigureAwait(false);
        }
        catch
        {
            await _terminated.Task.ConfigureAwait(false);
            return;
        }

        ShutdownCommand? shutdown;
        lock (_admissionGate)
        {
            if (_accepting)
            {
                _accepting = false;
                _shutdown = new ShutdownCommand();
                _commands.Enqueue(_shutdown);
            }

            shutdown = _shutdown;
            if (shutdown is not null && !shutdown.Task.IsCompleted)
            {
                _messageLoop.PostCommand(threadId);
            }
        }

        if (shutdown is not null)
        {
            await shutdown.Task.ConfigureAwait(false);
        }

        await _terminated.Task.ConfigureAwait(false);
    }

    private void Run()
    {
        Exception? failure = null;
        try
        {
            _ready.TrySetResult(_messageLoop.InitializeCurrentThread());
            while (_messageLoop.WaitForCommand())
            {
                while (_commands.TryDequeue(out var command))
                {
                    if (!command.Execute())
                    {
                        return;
                    }
                }
            }

            failure = new InvalidOperationException("The keyboard hook message loop stopped unexpectedly.");
        }
        catch (Exception exception)
        {
            failure = exception;
            _ready.TrySetException(exception);
        }
        finally
        {
            lock (_admissionGate)
            {
                _accepting = false;
            }

            FailPending(failure ?? new ObjectDisposedException(nameof(KeyboardHookThread)));
            _terminated.TrySetResult();
        }
    }

    private void FailPending(Exception exception)
    {
        while (_commands.TryDequeue(out var command))
        {
            command.Fail(exception);
        }
    }

    private abstract class Command
    {
        public abstract bool Execute();

        public abstract void Fail(Exception exception);
    }

    private sealed class OperationCommand<T>(Func<T> operation) : Command
    {
        private readonly TaskCompletionSource<T> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => _completion.Task;

        public override bool Execute()
        {
            if (_completion.Task.IsCompleted)
            {
                return true;
            }

            try
            {
                _completion.TrySetResult(operation());
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }

            return true;
        }

        public override void Fail(Exception exception) => _completion.TrySetException(exception);
    }

    private sealed class ShutdownCommand : Command
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Task => _completion.Task;

        public override bool Execute()
        {
            _completion.TrySetResult();
            return false;
        }

        public override void Fail(Exception exception) => _completion.TrySetException(exception);
    }
}
