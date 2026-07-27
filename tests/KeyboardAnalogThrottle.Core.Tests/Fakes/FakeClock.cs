using KeyboardAnalogThrottle.Core.Abstractions;

namespace KeyboardAnalogThrottle.Core.Tests.Fakes;

public sealed class FakeClock : IClock
{
    private readonly object _gate = new();
    private readonly List<DelayWaiter> _waiters = [];
    private TimeSpan _timestamp;

    public int PendingDelayCount
    {
        get
        {
            lock (_gate)
            {
                return _waiters.Count;
            }
        }
    }

    public TimeSpan GetTimestamp()
    {
        lock (_gate)
        {
            return _timestamp;
        }
    }

    public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (delay <= TimeSpan.Zero)
            {
                return ValueTask.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiter = new DelayWaiter(_timestamp + delay, completion);
            _waiters.Add(waiter);
            var registration = cancellationToken.Register(static state =>
            {
                var source = (TaskCompletionSource)state!;
                source.TrySetCanceled();
            }, completion);
            _ = completion.Task.ContinueWith(
                static (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                registration,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return new ValueTask(completion.Task);
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        }

        List<DelayWaiter>? completed = null;
        lock (_gate)
        {
            _timestamp += elapsed;
            for (var index = _waiters.Count - 1; index >= 0; index--)
            {
                if (_waiters[index].Due > _timestamp)
                {
                    continue;
                }

                completed ??= [];
                completed.Add(_waiters[index]);
                _waiters.RemoveAt(index);
            }
        }

        if (completed is not null)
        {
            foreach (var waiter in completed)
            {
                waiter.Completion.TrySetResult();
            }
        }
    }

    private sealed record DelayWaiter(TimeSpan Due, TaskCompletionSource Completion);
}
