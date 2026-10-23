using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Somning.Integration.Tests.Support;

public static class ForegroundHelper
{
    private static readonly HWND Topmost = new(-1);
    private static readonly HWND NotTopmost = new(-2);
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpShowWindow = 0x0040;

    public static bool EnsureForeground(nint window, nint editWindow)
    {
        var hwnd = new HWND(window);
        _ = PInvoke.SetForegroundWindow(hwnd);
        if (PInvoke.GetForegroundWindow() == hwnd) { _ = SetFocus(editWindow); return true; }

        var currentThread = PInvoke.GetCurrentThreadId();
        var targetThread = PInvoke.GetWindowThreadProcessId(hwnd, out _);
        var foregroundThread = PInvoke.GetWindowThreadProcessId(PInvoke.GetForegroundWindow(), out _);
        var attachedTarget = targetThread != 0 && targetThread != currentThread &&
            AttachThreadInput(currentThread, targetThread, true);
        var attachedForeground = foregroundThread != 0 && foregroundThread != currentThread &&
            foregroundThread != targetThread && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = BringWindowToTop(hwnd);
            _ = PInvoke.SetForegroundWindow(hwnd);
            _ = SetFocus(editWindow);
        }
        finally
        {
            if (attachedForeground) _ = AttachThreadInput(currentThread, foregroundThread, false);
            if (attachedTarget) _ = AttachThreadInput(currentThread, targetThread, false);
        }
        if (PInvoke.GetForegroundWindow() == hwnd) return true;

        _ = PInvoke.SetWindowPos(hwnd, Topmost, 0, 0, 0, 0,
            (Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS)(SwpNoMove | SwpNoSize | SwpShowWindow));
        _ = PInvoke.SetForegroundWindow(hwnd);
        _ = SetFocus(editWindow);
        _ = PInvoke.SetWindowPos(hwnd, NotTopmost, 0, 0, 0, 0,
            (Windows.Win32.UI.WindowsAndMessaging.SET_WINDOW_POS_FLAGS)(SwpNoMove | SwpNoSize | SwpShowWindow));
        return PInvoke.GetForegroundWindow() == hwnd;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachState);
    [DllImport("user32.dll", EntryPoint = "BringWindowToTop")] private static extern bool BringWindowToTop(HWND hwnd);
    [DllImport("user32.dll", EntryPoint = "SetFocus")] private static extern HWND SetFocus(nint hwnd);
}
