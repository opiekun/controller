using Microsoft.Win32.SafeHandles;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Interop;

internal sealed class SafeHookHandle : SafeHandleZeroOrMinusOneIsInvalid, IKeyboardHookRegistration
{
    internal SafeHookHandle(nint handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => NativeMethods.UnhookWindowsHookEx(handle);
}
