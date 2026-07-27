using System.Runtime.InteropServices;
using KeyboardAnalogThrottle.Core.Abstractions;
using KeyboardAnalogThrottle.Core.Configuration;
using KeyboardAnalogThrottle.Core.Emulation;
using KeyboardAnalogThrottle.Core.Input;
using KeyboardAnalogThrottle.Infrastructure.Windows.Interop;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Keyboard;

/// <summary>
/// Windows <c>WH_KEYBOARD_LL</c> adapter. The hook callback only updates local state,
/// evaluates suppression, and schedules notifications; it never touches controller or logging services.
/// </summary>
public sealed class LowLevelKeyboardInputSource : IKeyboardInputSource
{
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint LlkhfExtended = 0x01;
    private const int VkLeftShift = 0xA0;
    private const int VkRightShift = 0xA1;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;
    private const int VkLeftMenu = 0xA4;
    private const int VkRightMenu = 0xA5;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly KeyboardStateStore _stateStore = new();
    private readonly SuppressionPolicy _suppressionPolicy;
    private readonly IKeyboardHookPlatform _platform;
    private readonly Action<KeyboardHookCallbackStage>? _callbackStage;
    private readonly CaptureSuppressedKeys _suppressedKeys = new();
    private readonly List<KeyboardHookInstallation> _retiredInstallations = [];
    private IKeyboardHookRegistration? _hook;
    private KeyboardHookInstallation? _installation;
    private int _engineIsRunning;
    private int _disposed;

    public LowLevelKeyboardInputSource(AppConfiguration configuration)
        : this(configuration, new Win32KeyboardHookPlatform())
    {
    }

    internal LowLevelKeyboardInputSource(
        AppConfiguration configuration,
        IKeyboardHookPlatform platform,
        Action<KeyboardHookCallbackStage>? callbackStage = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(platform);
        _suppressionPolicy = new SuppressionPolicy(configuration);
        _platform = platform;
        _callbackStage = callbackStage;
    }

    public InputHealth Health => Volatile.Read(ref _disposed) != 0 ? InputHealth.Unavailable : _stateStore.Health;

    public event EventHandler<KeyStateChangedEventArgs>? KeyStateChanged;

    /// <summary>
    /// Updates cached engine state from composition code. The hook callback only reads this value.
    /// </summary>
    public void SetEngineRunning(bool isRunning) => Volatile.Write(ref _engineIsRunning, isRunning ? 1 : 0);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_installation is not null)
            {
                return;
            }

            var captureGeneration = _stateStore.BeginCapture();
            var capture = _suppressedKeys.BeginCapture(captureGeneration);
            var installation = new KeyboardHookInstallation(this, capture, _callbackStage);
            IKeyboardHookRegistration? hook = null;
            try
            {
                hook = _platform.Install(installation.Callback);
                _hook = hook;
                _installation = installation;
                SynchronizeCurrentModifiers();
                _stateStore.SetHealth(InputHealth.Healthy);
                installation.Activate();
            }
            catch
            {
                installation.CloseAdmission();
                hook?.Dispose();
                _hook = null;
                _installation = null;
                _retiredInstallations.Add(installation);
                _suppressedKeys.EndCapture(capture);
                _stateStore.StopCapture();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetEngineRunning(isRunning: false);
            var installation = _installation;
            var hook = _hook;
            _installation = null;
            _hook = null;
            installation?.CloseAdmission();
            hook?.Dispose();

            if (installation is not null)
            {
                _retiredInstallations.Add(installation);
                await installation.WaitForQuiescenceAsync().ConfigureAwait(false);
                _suppressedKeys.EndCapture(installation.Capture);
            }
            else
            {
                _suppressedKeys.EndCapture();
            }

            _stateStore.StopCapture();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public InputSnapshot GetSnapshot() => _stateStore.GetSnapshot();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private nint HookCallback(
        KeyboardHookInstallation installation,
        int code,
        nuint wParam,
        nint lParam)
    {
        if (code < 0)
        {
            return _platform.CallNext(code, wParam, lParam);
        }

        try
        {
            var message = (uint)wParam;
            var isDown = message is WmKeyDown or WmSysKeyDown;
            var isUp = message is WmKeyUp or WmSysKeyUp;
            if (!isDown && !isUp)
            {
                return _platform.CallNext(code, wParam, lParam);
            }

            var nativeKey = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var key = MapKey(nativeKey);
            if (key == InputKey.None)
            {
                return _platform.CallNext(code, wParam, lParam);
            }

            var capture = installation.Capture;
            if (!_suppressedKeys.IsCurrent(capture))
            {
                return _platform.CallNext(code, wParam, lParam);
            }

            var captureGeneration = capture.Generation;
            var notification = isDown
                ? _stateStore.TryApplyDown(key, captureGeneration)
                : _stateStore.TryApplyUp(key, captureGeneration);
            if (!_stateStore.IsCurrentCapture(captureGeneration))
            {
                return _platform.CallNext(code, wParam, lParam);
            }

            var suppress = isDown ? ShouldSuppressDown(key, capture) : WasSuppressedOnDown(key, capture);
            if (notification is not null)
            {
                DispatchNotification(notification);
            }

            _callbackStage?.Invoke(KeyboardHookCallbackStage.BeforeFinalResult);
            return suppress &&
                installation.IsAccepting &&
                _suppressedKeys.IsCurrent(capture)
                ? 1
                : _platform.CallNext(code, wParam, lParam);
        }
        catch
        {
            // A hook must fail open; exceptions may not escape into the Windows callback chain.
            return _platform.CallNext(code, wParam, lParam);
        }
    }

    private bool ShouldSuppressDown(InputKey key, CaptureSuppressedKeys.Session capture)
    {
        if (!_suppressedKeys.IsCurrent(capture))
        {
            return false;
        }

        var suppress = _suppressionPolicy.ShouldSuppress(
            key,
            _stateStore.SuppressionState.Modifiers,
            Volatile.Read(ref _engineIsRunning) != 0);
        if (suppress && _stateStore.IsCurrentCapture(capture.Generation))
        {
            return _suppressedKeys.TryMark(key, capture);
        }

        return false;
    }

    private bool WasSuppressedOnDown(InputKey key, CaptureSuppressedKeys.Session capture)
        => _suppressedKeys.TryTake(key, capture);

    private void SynchronizeCurrentModifiers()
    {
        var modifiers = InputModifiers.None;
        if (_platform.IsVirtualKeyDown(VkLeftControl) || _platform.IsVirtualKeyDown(VkRightControl)) modifiers |= InputModifiers.Control;
        if (_platform.IsVirtualKeyDown(VkLeftMenu) || _platform.IsVirtualKeyDown(VkRightMenu)) modifiers |= InputModifiers.Alt;
        if (_platform.IsVirtualKeyDown(VkLeftShift) || _platform.IsVirtualKeyDown(VkRightShift)) modifiers |= InputModifiers.Shift;
        _stateStore.SynchronizeModifiers(modifiers);
    }

    private static InputKey MapKey(KbdLlHookStruct nativeKey)
    {
        var virtualKey = nativeKey.VkCode;
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            return InputKey.A + (int)(virtualKey - 0x41);
        }

        if (virtualKey is >= 0x30 and <= 0x39)
        {
            return InputKey.D0 + (int)(virtualKey - 0x30);
        }

        if (virtualKey is >= 0x70 and <= 0x7B)
        {
            return InputKey.F1 + (int)(virtualKey - 0x70);
        }

        return virtualKey switch
        {
            0x20 => InputKey.Space,
            0x21 => InputKey.PageUp,
            0x22 => InputKey.PageDown,
            0x24 => InputKey.Home,
            VkLeftControl => InputKey.LeftControl,
            VkRightControl => InputKey.RightControl,
            VkLeftMenu => InputKey.LeftAlt,
            VkRightMenu => InputKey.RightAlt,
            VkLeftShift => InputKey.LeftShift,
            VkRightShift => InputKey.RightShift,
            0x11 => (nativeKey.Flags & LlkhfExtended) != 0 ? InputKey.RightControl : InputKey.LeftControl,
            0x12 => (nativeKey.Flags & LlkhfExtended) != 0 ? InputKey.RightAlt : InputKey.LeftAlt,
            0x10 => nativeKey.ScanCode == 0x36 ? InputKey.RightShift : InputKey.LeftShift,
            _ => InputKey.None
        };
    }

    private void DispatchNotification(KeyStateChangedEventArgs notification)
    {
        var handlers = KeyStateChanged;
        if (handlers is null)
        {
            return;
        }

        ThreadPool.UnsafeQueueUserWorkItem(
            static state => state.Source.RaiseStateChanged(state.Handlers, state.Notification),
            (Source: this, Handlers: handlers, Notification: notification),
            preferLocal: false);
    }

    private void RaiseStateChanged(EventHandler<KeyStateChangedEventArgs> handlers, KeyStateChangedEventArgs notification)
    {
        foreach (EventHandler<KeyStateChangedEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, notification);
            }
            catch
            {
                // Subscriber failures must not affect keyboard capture.
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(LowLevelKeyboardInputSource));
        }
    }

    private sealed class KeyboardHookInstallation
    {
        private readonly LowLevelKeyboardInputSource _source;
        private readonly Action<KeyboardHookCallbackStage>? _callbackStage;
        private readonly TaskCompletionSource _quiesced =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _accepting;
        private int _inFlight;

        public KeyboardHookInstallation(
            LowLevelKeyboardInputSource source,
            CaptureSuppressedKeys.Session capture,
            Action<KeyboardHookCallbackStage>? callbackStage)
        {
            _source = source;
            Capture = capture;
            _callbackStage = callbackStage;
            Callback = Invoke;
        }

        public CaptureSuppressedKeys.Session Capture { get; }

        public NativeMethods.HookProcedure Callback { get; }

        public bool IsAccepting => Volatile.Read(ref _accepting) != 0;

        public void Activate() => Volatile.Write(ref _accepting, 1);

        public void CloseAdmission()
        {
            Volatile.Write(ref _accepting, 0);
            if (Volatile.Read(ref _inFlight) == 0)
            {
                _quiesced.TrySetResult();
            }
        }

        public Task WaitForQuiescenceAsync() => _quiesced.Task;

        private nint Invoke(int code, nuint wParam, nint lParam)
        {
            try
            {
                _callbackStage?.Invoke(KeyboardHookCallbackStage.BeforeAdmission);
            }
            catch
            {
                return _source._platform.CallNext(code, wParam, lParam);
            }

            Interlocked.Increment(ref _inFlight);
            if (!IsAccepting)
            {
                Exit();
                return _source._platform.CallNext(code, wParam, lParam);
            }

            try
            {
                return _source.HookCallback(this, code, wParam, lParam);
            }
            finally
            {
                Exit();
            }
        }

        private void Exit()
        {
            if (Interlocked.Decrement(ref _inFlight) == 0 && !IsAccepting)
            {
                _quiesced.TrySetResult();
            }
        }
    }
}

internal enum KeyboardHookCallbackStage
{
    BeforeAdmission,
    BeforeFinalResult
}
