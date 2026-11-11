using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CopilotUsageMeter.Tray;

// Windows blocks background processes from taking the foreground, so a plain Activate() only flashes the taskbar button when the request
// came from Visual Studio. Attaching to the foreground thread's input queue is the documented way for the tray to bring its own window forward.
internal static class ForegroundWindow
{
    private const int SwRestore = 9;
    private const byte VkF24 = 0x87;
    private const uint KeyUp = 0x0002;

    public static void Bring(Window window)
    {
        if (!window.IsVisible) window.Show();
        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (IsIconic(handle)) ShowWindow(handle, SwRestore);
        if (TryForeground(handle)) return;
        // A synthetic key press counts as user input and lifts the foreground lock for this process.
        keybd_event(VkF24, 0, 0, UIntPtr.Zero);
        keybd_event(VkF24, 0, KeyUp, UIntPtr.Zero);
        if (TryForeground(handle)) return;
        // Last resort: a brief topmost flip raises the window above others even if it cannot take focus.
        window.Topmost = true;
        window.Topmost = false;
        window.Activate();
    }

    private static bool TryForeground(IntPtr handle)
    {
        var foreground = GetForegroundWindow();
        if (foreground == handle) return true;
        var currentThread = GetCurrentThreadId();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, foregroundThread, false);
        }
        return GetForegroundWindow() == handle;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
