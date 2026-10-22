using System.Runtime.InteropServices;
namespace Somning.Integration.Tests.Support;
public static class ClipboardProbe
{
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    public static uint SequenceNumber() => GetClipboardSequenceNumber();
    public static string ReadText()
    {
        bool opened = false;
        for (int attempt = 0; attempt < 10; attempt++)
        { if (OpenClipboard(0)) { opened = true; break; } Thread.Sleep(20); }
        if (!opened) throw new IOException("Clipboard unavailable.");
        try
        {
            var memory = GetClipboardData(13);
            if (memory == 0) return "";
            var pointer = GlobalLock(memory);
            if (pointer == 0) throw new IOException("Clipboard data unavailable.");
            try { return Marshal.PtrToStringUni(pointer) ?? ""; }
            finally { GlobalUnlock(memory); }
        }
        finally { CloseClipboard(); }
    }
}
