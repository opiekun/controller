using System.Collections.Concurrent;
using KeyboardAnalogThrottle.Infrastructure.Windows.Interop;

namespace KeyboardAnalogThrottle.Core.Tests.Input;

[Collection(KeyboardHookLifecycleCollection.Name)]
public sealed class KeyboardHookThreadTests
{
    [Fact]
    public async Task Win32_message_loop_executes_a_command_without_installing_a_hook()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        await using var hookThread = new KeyboardHookThread();

        var executionThreadId = await hookThread.InvokeAsync(
            static () => Environment.CurrentManagedThreadId);

        Assert.NotEqual(callerThreadId, executionThreadId);
    }

    [Fact]
    public async Task Commands_execute_in_fifo_order_on_one_dedicated_thread()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var messageLoop = new FakeKeyboardHookMessageLoop();
        await using var hookThread = new KeyboardHookThread(messageLoop);
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var order = new List<int>();
        var executionThreadIds = new List<int>();

        var first = hookThread.InvokeAsync(() =>
        {
            executionThreadIds.Add(Environment.CurrentManagedThreadId);
            order.Add(1);
            firstStarted.Set();
            releaseFirst.Wait();
        });
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));

        var second = hookThread.InvokeAsync(() =>
        {
            executionThreadIds.Add(Environment.CurrentManagedThreadId);
            order.Add(2);
        });
        var third = hookThread.InvokeAsync(() =>
        {
            executionThreadIds.Add(Environment.CurrentManagedThreadId);
            order.Add(3);
        });

        releaseFirst.Set();
        await Task.WhenAll(first, second, third);

        Assert.Equal([1, 2, 3], order);
        Assert.All(executionThreadIds, id => Assert.Equal(executionThreadIds[0], id));
        Assert.NotEqual(callerThreadId, executionThreadIds[0]);
        Assert.All(messageLoop.PostedThreadIds, id => Assert.Equal(messageLoop.OwnerThreadId, id));
    }

    [Fact]
    public async Task Disposal_drains_admitted_commands_then_rejects_new_commands()
    {
        var messageLoop = new FakeKeyboardHookMessageLoop();
        var hookThread = new KeyboardHookThread(messageLoop);
        using var commandStarted = new ManualResetEventSlim();
        using var releaseCommand = new ManualResetEventSlim();
        var commandCompleted = false;

        var command = hookThread.InvokeAsync(() =>
        {
            commandStarted.Set();
            releaseCommand.Wait();
            commandCompleted = true;
        });
        Assert.True(commandStarted.Wait(TimeSpan.FromSeconds(5)));

        var dispose = hookThread.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        releaseCommand.Set();
        await dispose;
        await command;

        Assert.True(commandCompleted);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => hookThread.InvokeAsync(static () => { }));
    }

    [Fact]
    public async Task Failed_wake_does_not_strand_the_next_command()
    {
        var messageLoop = new FakeKeyboardHookMessageLoop { PostsToFail = 1 };
        await using var hookThread = new KeyboardHookThread(messageLoop);
        var failedCommandRan = false;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hookThread.InvokeAsync(() => failedCommandRan = true));
        var result = await hookThread
            .InvokeAsync(static () => 42)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(failedCommandRan);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Disposal_can_be_retried_after_a_failed_wake()
    {
        var messageLoop = new FakeKeyboardHookMessageLoop { PostsToFail = 1 };
        var hookThread = new KeyboardHookThread(messageLoop);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => hookThread.DisposeAsync().AsTask());
        await hookThread.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => hookThread.InvokeAsync(static () => { }));
    }

    private sealed class FakeKeyboardHookMessageLoop : IKeyboardHookMessageLoop
    {
        private readonly BlockingCollection<bool> _signals = [];
        private int _postsToFail;

        public uint OwnerThreadId { get; private set; }

        public ConcurrentQueue<uint> PostedThreadIds { get; } = [];

        public int PostsToFail
        {
            init => _postsToFail = value;
        }

        public uint InitializeCurrentThread()
        {
            OwnerThreadId = (uint)Environment.CurrentManagedThreadId;
            return OwnerThreadId;
        }

        public bool WaitForCommand() => _signals.Take();

        public void PostCommand(uint threadId)
        {
            PostedThreadIds.Enqueue(threadId);
            if (Interlocked.Decrement(ref _postsToFail) >= 0)
            {
                throw new InvalidOperationException("Synthetic post failure.");
            }

            _signals.Add(true);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KeyboardHookLifecycleCollection
{
    public const string Name = "Keyboard hook lifecycle";
}
