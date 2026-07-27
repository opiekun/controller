using System.Windows.Input;

namespace KeyboardAnalogThrottle.App.Commands;

/// <summary>
/// An asynchronous command that prevents concurrent execution of itself.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly SynchronizationContext? _synchronizationContext;
    private int _isExecuting;

    public AsyncRelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        SynchronizationContext? synchronizationContext = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _synchronizationContext = synchronizationContext;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting => Volatile.Read(ref _isExecuting) != 0;

    public bool CanExecute(object? parameter) => !IsExecuting && (_canExecute?.Invoke() ?? true);

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter) || Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
        {
            return;
        }

        RaiseCanExecuteChanged();
        try
        {
            await _execute().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
        }
    }

    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    public void RaiseCanExecuteChanged()
    {
        if (_synchronizationContext is null || SynchronizationContext.Current == _synchronizationContext)
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _synchronizationContext.Post(
            static state => ((AsyncRelayCommand)state!).CanExecuteChanged?.Invoke(state, EventArgs.Empty),
            this);
    }
}
