using System.ComponentModel;
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
    private const int WhKeyboardLl = 13;
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

    private readonly object _lifecycleGate = new();
    private readonly object _suppressionGate = new();
    private readonly KeyboardStateStore _stateStore = new();
    private readonly SuppressionPolicy _suppressionPolicy;
    private readonly Func<bool> _isEngineRunning;
    private readonly NativeMethods.HookProcedure _hookProcedure;
    private readonly HashSet<InputKey> _suppressedKeys = [];
    private SafeHookHandle? _hook;
    private int _acceptingInput;
    private int _disposed;

    public LowLevelKeyboardInputSource(AppConfiguration configuration, Func<bool> isEngineRunning)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _isEngineRunning = isEngineRunning ?? throw new ArgumentNullException(nameof(isEngineRunning));
        _suppressionPolicy = new SuppressionPolicy(configuration);
        _hookProcedure = HookCallback;
    }

    public InputHealth Health => Volatile.Read(ref _disposed) != 0 ? InputHealth.Unavailable : _stateStore.Health;

    public event EventHandler<KeyStateChangedEventArgs>? KeyStateChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            if (_hook is not null && !_hook.IsInvalid && !_hook.IsClosed)
            {
                return Task.CompletedTask;
            }

            _stateStore.Clear(InputHealth.Synchronizing);
            var nativeHandle = NativeMethods.SetWindowsHookEx(
                WhKeyboardLl,
                _hookProcedure,
                NativeMethods.GetModuleHandle(null),
                threadId: 0);

            if (nativeHandle == nint.Zero)
            {
                _stateStore.SetHealth(InputHealth.Unavailable);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the low-level keyboard hook.");
            }

            _hook = new SafeHookHandle(nativeHandle);
            SynchronizeCurrentModifiers();
            _stateStore.SetHealth(InputHealth.Healthy);
            Volatile.Write(ref _acceptingInput, 1);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        SafeHookHandle? hook;
        lock (_lifecycleGate)
        {
            Volatile.Write(ref _acceptingInput, 0);
            hook = _hook;
            _hook = null;
            lock (_suppressionGate)
            {
                _suppressedKeys.Clear();
            }

            _stateStore.Clear(InputHealth.Unavailable);
        }

        hook?.Dispose();
        return Task.CompletedTask;
    }

    public InputSnapshot GetSnapshot() => _stateStore.GetSnapshot();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private nint HookCallback(int code, nuint wParam, nint lParam)
    {
        if (code < 0 || Volatile.Read(ref _acceptingInput) == 0)
        {
            return NativeMethods.CallNextHookEx(nint.Zero, code, wParam, lParam);
        }

        try
        {
            var message = (uint)wParam;
            var isDown = message is WmKeyDown or WmSysKeyDown;
            var isUp = message is WmKeyUp or WmSysKeyUp;
            if (!isDown && !isUp)
            {
                return NativeMethods.CallNextHookEx(nint.Zero, code, wParam, lParam);
            }

            var nativeKey = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var key = MapKey(nativeKey);
            if (key == InputKey.None)
            {
                return NativeMethods.CallNextHookEx(nint.Zero, code, wParam, lParam);
            }

            var notification = isDown ? _stateStore.ApplyDown(key) : _stateStore.ApplyUp(key);
            var suppress = isDown ? ShouldSuppressDown(key) : WasSuppressedOnDown(key);
            if (notification is not null)
            {
                DispatchNotification(notification);
            }

            return suppress
                ? 1
                : NativeMethods.CallNextHookEx(nint.Zero, code, wParam, lParam);
        }
        catch
        {
            // A hook must fail open; exceptions may not escape into the Windows callback chain.
            return NativeMethods.CallNextHookEx(nint.Zero, code, wParam, lParam);
        }
    }

    private bool ShouldSuppressDown(InputKey key)
    {
        var engineIsRunning = false;
        try
        {
            engineIsRunning = _isEngineRunning();
        }
        catch
        {
            return false;
        }

        var suppress = _suppressionPolicy.ShouldSuppress(key, _stateStore.PeekSnapshot(), engineIsRunning);
        if (suppress)
        {
            lock (_suppressionGate)
            {
                _suppressedKeys.Add(key);
            }
        }

        return suppress;
    }

    private bool WasSuppressedOnDown(InputKey key)
    {
        lock (_suppressionGate)
        {
            return _suppressedKeys.Remove(key);
        }
    }

    private void SynchronizeCurrentModifiers()
    {
        var modifiers = InputModifiers.None;
        if (IsVirtualKeyDown(VkLeftControl) || IsVirtualKeyDown(VkRightControl)) modifiers |= InputModifiers.Control;
        if (IsVirtualKeyDown(VkLeftMenu) || IsVirtualKeyDown(VkRightMenu)) modifiers |= InputModifiers.Alt;
        if (IsVirtualKeyDown(VkLeftShift) || IsVirtualKeyDown(VkRightShift)) modifiers |= InputModifiers.Shift;
        _stateStore.SynchronizeModifiers(modifiers);
    }

    private static bool IsVirtualKeyDown(int virtualKey) => (NativeMethods.GetKeyState(virtualKey) & 0x8000) != 0;

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
}
