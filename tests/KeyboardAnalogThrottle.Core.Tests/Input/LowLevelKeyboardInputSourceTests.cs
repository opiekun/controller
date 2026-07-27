using System.Runtime.InteropServices;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Infrastructure.Windows.Interop;
using KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

namespace KeyboardAnalogThrottle.Core.Tests.Input;

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

        await source.StopAsync(CancellationToken.None);
        await source.StartAsync(CancellationToken.None);
        source.SetEngineRunning(isRunning: true);
        continueCallback.Set();

        Assert.Equal(PassedThrough, await callbackResult);
        Assert.False(source.GetSnapshot().IsPressed(Core.Input.InputKey.W));
        Assert.Equal(2, platform.InstalledCallbacks.Count);
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

        public IKeyboardHookRegistration Install(NativeMethods.HookProcedure callback)
        {
            InstalledCallbacks.Add(callback);
            return new Registration();
        }

        public nint CallNext(int code, nuint wParam, nint lParam) => passedThroughResult;

        public bool IsVirtualKeyDown(int virtualKey) => false;

        private sealed class Registration : IKeyboardHookRegistration
        {
            public void Dispose()
            {
            }
        }
    }
}
