using System.Runtime.InteropServices;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct KbdLlHookStruct
{
    public uint VkCode;

    public uint ScanCode;

    public uint Flags;

    public uint Time;

    public nint DwExtraInfo;
}
