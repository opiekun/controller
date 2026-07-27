using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Interop;

internal interface IKeyboardHookRegistration : IDisposable;

internal interface IKeyboardHookPlatform
{
    IKeyboardHookRegistration Install(NativeMethods.HookProcedure callback);

    nint CallNext(int code, nuint wParam, nint lParam);

    bool IsVirtualKeyDown(int virtualKey);
}

internal sealed class Win32KeyboardHookPlatform : IKeyboardHookPlatform
{
    private const int WhKeyboardLl = 13;

    public IKeyboardHookRegistration Install(NativeMethods.HookProcedure callback)
    {
        var nativeHandle = NativeMethods.SetWindowsHookEx(
            WhKeyboardLl,
            callback,
            NativeMethods.GetModuleHandle(null),
            threadId: 0);

        if (nativeHandle == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the low-level keyboard hook.");
        }

        return new SafeHookHandle(nativeHandle);
    }

    public nint CallNext(int code, nuint wParam, nint lParam) =>
        NativeMethods.CallNextHookEx(nint.Zero, code, wParam, lParam);

    public bool IsVirtualKeyDown(int virtualKey) =>
        (NativeMethods.GetKeyState(virtualKey) & 0x8000) != 0;
}
