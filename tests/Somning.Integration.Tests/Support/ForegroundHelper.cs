using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using PInvoke = Windows.Win32.IntegrationPInvoke;

namespace Somning.Integration.Tests.Support;

public static class ForegroundHelper
{
    private static readonly HWND Topmost = new(-1);
    private static readonly HWND NotTopmost = new(-2);
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpShowWindow = 0x0040;

    /// <summary>
    /// Retries the acquisition for up to 1 s: right after other test windows close (e.g. two Notepads), Windows can
    /// briefly refuse foreground changes, which made a single attempt order-dependent in full-suite runs (01-11).
    /// </summary>
    public static bool EnsureForeground(nint window, nint editWindow)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (true)
        {
            if (TryEnsureForeground(window, editWindow)) return true;
            if (DateTime.UtcNow > until) return false;
            Thread.Sleep(50);
        }
    }

    /// <summary>Waits until Win32 reports the requested edit as the active focused control.</summary>
    public static unsafe bool EnsureForegroundFocus(nint window, nint editWindow)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        do
        {
            if (TryEnsureForeground(window, editWindow))
            {
                var thread = PInvoke.GetWindowThreadProcessId(new HWND(window), out _);
                var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
                if (thread != 0 && PInvoke.GetGUIThreadInfo(thread, ref info) && info.hwndFocus.Value != null)
                    return true;
            }
            Thread.Sleep(25);
        } while (DateTime.UtcNow < until);
        return false;
    }

    /// <summary>The current foreground window handle (0 when none).</summary>
    public static unsafe nint ForegroundWindow() => (nint)PInvoke.GetForegroundWindow().Value;

    /// <summary>The focused control of the thread that owns <paramref name="window"/> (0 when none or unknown).</summary>
    public static unsafe nint FocusedControl(nint window)
    {
        var thread = PInvoke.GetWindowThreadProcessId(new HWND(window), out _);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        return thread != 0 && PInvoke.GetGUIThreadInfo(thread, ref info) ? (nint)info.hwndFocus.Value : 0;
    }

    /// <summary>Diagnostic string naming the foreground window (title/class) and the focused control of <paramref name="window"/>.</summary>
    public static string Describe(nint window)
    {
        var foreground = ForegroundWindow();
        var title = new System.Text.StringBuilder(256);
        var className = new System.Text.StringBuilder(256);
        if (foreground != 0)
        {
            _ = GetWindowText(foreground, title, title.Capacity);
            _ = GetClassName(foreground, className, className.Capacity);
        }
        return $"foreground=0x{foreground:X} title='{title}' class='{className}'; " +
            $"expected window=0x{window:X}; focused control=0x{FocusedControl(window):X}";
    }

    private static bool TryEnsureForeground(nint window, nint editWindow)
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint hwnd, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachState);
    [DllImport("user32.dll", EntryPoint = "BringWindowToTop")] private static extern bool BringWindowToTop(HWND hwnd);
    [DllImport("user32.dll", EntryPoint = "SetFocus")] private static extern HWND SetFocus(nint hwnd);
}
