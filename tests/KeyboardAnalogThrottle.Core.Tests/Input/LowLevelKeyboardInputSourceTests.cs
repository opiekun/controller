using System.Runtime.InteropServices;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Infrastructure.Windows.Interop;
using KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

namespace KeyboardAnalogThrottle.Core.Tests.Input;

[Collection(KeyboardHookLifecycleCollection.Name)]
public sealed class LowLevelKeyboardInputSourceTests
{
    private const nint PassedThrough = 73;

    [Fact]
    public async Task Callback_from_unhooked_installation_paused_before_admission_cannot_mutate_replacement_capture()
    {
        using var callbackReachedEntry = new ManualResetEventSlim();
        using var continueCallback = new ManualResetEventSlim();
        var pauseNextEntry = 1;
        var platform = new FakeKeyboardHookPlatform(PassedThrough);
        await using var source = new LowLevelKeyboardInputSource(
            Configuration(),
            platform,
            stage =>
            {
                if (stage == KeyboardHookCallbackStage.BeforeAdmission &&
                    Interlocked.Exchange(ref pauseNextEntry, 0) == 1)
                {
                    callbackReachedEntry.Set();
                    continueCallback.Wait();
                }
            });

        await source.StartAsync(CancellationToken.None);
        source.SetEngineRunning(isRunning: true);
        var oldCallback = platform.InstalledCallbacks[0];
        var callbackResult = Task.Run(() => InvokeKeyDown(oldCallback));
        Assert.True(callbackReachedEntry.Wait(TimeSpan.FromSeconds(5)));

        var stop = source.StopAsync(CancellationToken.None);
        Assert.False(stop.IsCompleted);
        continueCallback.Set();
        Assert.Equal(PassedThrough, await callbackResult);
        await stop;
        await source.StartAsync(CancellationToken.None);
        source.SetEngineRunning(isRunning: true);

        Assert.False(source.GetSnapshot().IsPressed(Core.Input.InputKey.W));
        Assert.Equal(2, platform.InstalledCallbacks.Count);
    }

    [Fact]
    public async Task Start_stop_restart_installs_and_unhooks_on_one_dedicated_thread_in_order()
    {
        var callerThreadId = Environment.CurrentManagedThreadId;
        var messageLoop = new FakeKeyboardHookMessageLoop();
        var hookThread = new KeyboardHookThread(messageLoop);
        var platform = new FakeKeyboardHookPlatform(PassedThrough);
        var source = new LowLevelKeyboardInputSource(
            Configuration(),
            platform,
            hookThread: hookThread);

        await source.StartAsync(CancellationToken.None);
        await source.StartAsync(CancellationToken.None);
        await source.StopAsync(CancellationToken.None);
        await source.StopAsync(CancellationToken.None);
        await source.StartAsync(CancellationToken.None);
        await source.DisposeAsync();

        Assert.Equal(["install", "unhook", "install", "unhook"], platform.LifecycleOperations);
        Assert.Equal(2, platform.InstalledCallbacks.Count);
        Assert.NotSame(platform.InstalledCallbacks[0], platform.InstalledCallbacks[1]);
        Assert.Equal(4, platform.LifecycleThreadIds.Count);
        Assert.All(platform.LifecycleThreadIds, id => Assert.Equal(platform.LifecycleThreadIds[0], id));
        Assert.NotEqual(callerThreadId, platform.LifecycleThreadIds[0]);
    }

    [Fact]
    public async Task Stop_waits_for_a_suppression_result_in_progress_and_forces_it_to_fail_open()
    {
        using var callbackReachedFinalResult = new ManualResetEventSlim();
        using var continueCallback = new ManualResetEventSlim();
        var platform = new FakeKeyboardHookPlatform(PassedThrough);
        await using var source = new LowLevelKeyboardInputSource(
            Configuration(),
            platform,
            stage =>
            {
                if (stage == KeyboardHookCallbackStage.BeforeFinalResult)
                {
                    callbackReachedFinalResult.Set();
                    continueCallback.Wait();
                }
            });

        await source.StartAsync(CancellationToken.None);
        source.SetEngineRunning(isRunning: true);
        var callbackResult = Task.Run(() => InvokeKeyDown(platform.InstalledCallbacks[0]));
        Assert.True(callbackReachedFinalResult.Wait(TimeSpan.FromSeconds(5)));

        var stop = source.StopAsync(CancellationToken.None);

        Assert.True(platform.Unhooked.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(stop.IsCompleted);
        Assert.Equal(InputHealth.Healthy, source.Health);
        continueCallback.Set();
        Assert.Equal(PassedThrough, await callbackResult);
        await stop;
        Assert.Equal(InputHealth.Unavailable, source.Health);
        Assert.False(source.GetSnapshot().IsPressed(Core.Input.InputKey.W));
    }

    private static AppConfiguration Configuration() => new()
    {
        Input = new InputConfiguration
        {
            SuppressMappedKeys = true,
            ThrottleCutBinding = "Space",
            EmergencyDisableBinding = "Ctrl+Alt+F12"
        },
        Throttle = ChannelConfiguration.CreateThrottleDefault(),
        Brake = ChannelConfiguration.CreateBrakeDefault(),
        Ratchet = RatchetConfiguration.Default
    };

    private static nint InvokeKeyDown(NativeMethods.HookProcedure callback)
    {
        var nativeKey = new KbdLlHookStruct { VkCode = 0x57 };
        var nativeKeyPointer = Marshal.AllocHGlobal(Marshal.SizeOf<KbdLlHookStruct>());
        try
        {
            Marshal.StructureToPtr(nativeKey, nativeKeyPointer, fDeleteOld: false);
            return callback(0, 0x0100, nativeKeyPointer);
        }
        finally
        {
            Marshal.FreeHGlobal(nativeKeyPointer);
        }
    }

    private sealed class FakeKeyboardHookPlatform(nint passedThroughResult) : IKeyboardHookPlatform
    {
        public List<NativeMethods.HookProcedure> InstalledCallbacks { get; } = [];

        public List<string> LifecycleOperations { get; } = [];

        public List<int> LifecycleThreadIds { get; } = [];

        public ManualResetEventSlim Unhooked { get; } = new();

        public IKeyboardHookRegistration Install(NativeMethods.HookProcedure callback)
        {
            InstalledCallbacks.Add(callback);
            LifecycleOperations.Add("install");
            LifecycleThreadIds.Add(Environment.CurrentManagedThreadId);
            return new Registration(this);
        }

        public nint CallNext(int code, nuint wParam, nint lParam) => passedThroughResult;

        public bool IsVirtualKeyDown(int virtualKey) => false;

        private sealed class Registration(FakeKeyboardHookPlatform owner) : IKeyboardHookRegistration
        {
            public void Dispose()
            {
                owner.LifecycleOperations.Add("unhook");
                owner.LifecycleThreadIds.Add(Environment.CurrentManagedThreadId);
                owner.Unhooked.Set();
            }
        }
    }

    private sealed class FakeKeyboardHookMessageLoop : IKeyboardHookMessageLoop
    {
        private readonly System.Collections.Concurrent.BlockingCollection<bool> _signals = [];

        public uint InitializeCurrentThread() => (uint)Environment.CurrentManagedThreadId;

        public bool WaitForCommand() => _signals.Take();

        public void PostCommand(uint threadId) => _signals.Add(true);
    }
}
