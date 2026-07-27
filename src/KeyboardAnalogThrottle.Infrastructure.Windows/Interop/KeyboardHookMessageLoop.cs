using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyboardAnalogThrottle.Infrastructure.Windows.Interop;

internal interface IKeyboardHookMessageLoop
{
    uint InitializeCurrentThread();

    bool WaitForCommand();

    void PostCommand(uint threadId);
}

internal sealed class Win32KeyboardHookMessageLoop : IKeyboardHookMessageLoop
{
    private const uint CommandMessage = 0x8001;
    private const uint PmNoRemove = 0x0000;

    public uint InitializeCurrentThread()
    {
        // Force Windows to create this thread's message queue before another
        // thread can attempt PostThreadMessage.
        NativeMethods.PeekMessage(
            out _,
            nint.Zero,
            minMessage: 0,
            maxMessage: 0,
            PmNoRemove);
        return NativeMethods.GetCurrentThreadId();
    }

    public bool WaitForCommand()
    {
        while (true)
        {
            var result = NativeMethods.GetMessage(
                out var message,
                nint.Zero,
                minMessage: 0,
                maxMessage: 0);
            if (result == -1)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "The keyboard hook message loop could not retrieve its next message.");
            }

            if (result == 0)
            {
                return false;
            }

            if (message.Message == CommandMessage)
            {
                return true;
            }

            NativeMethods.TranslateMessage(in message);
            NativeMethods.DispatchMessage(in message);
        }
    }

    public void PostCommand(uint threadId)
    {
        if (!NativeMethods.PostThreadMessage(threadId, CommandMessage, nuint.Zero, nint.Zero))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to post a command to the keyboard hook thread.");
        }
    }
}
