using System.Runtime.InteropServices;
using System.Text;
namespace Somning.Platform.Windows.Clipboard;
public sealed record ClipboardSetResult(bool Success, uint SequenceBefore, uint SequenceAfter, int Attempts, string? Error);
public sealed class ClipboardWriter : IDisposable
{
    private readonly MessageOnlyWindow owner = new();
    public ClipboardSetResult SetText(string text)
    {
        try { return owner.Invoke(() => WriteText(text)); }
        catch (Exception) { return new(false, 0, 0, 0, "clipboard_unavailable"); }
    }
    private ClipboardSetResult WriteText(string text)
    {
        uint before = Native.GetClipboardSequenceNumber(); int attempts = 0; bool opened = false;
        nint memory = 0; string? error = null;
        try
        {
            for (attempts = 1; attempts <= 10; attempts++)
            {
                if (Native.OpenClipboard(owner.Handle)) { opened = true; break; }
                if (attempts < 10) Thread.Sleep(10 << Math.Min(attempts - 1, 4));
            }
            if (!opened) return new(false, before, Native.GetClipboardSequenceNumber(), 10, "clipboard_busy");
            if (!Native.EmptyClipboard()) throw new InvalidOperationException("clipboard_empty_failed");
            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            memory = Native.GlobalAlloc(2, (nuint)bytes.Length);
            if (memory == 0) throw new InvalidOperationException("clipboard_allocation_failed");
            var pointer = Native.GlobalLock(memory);
            if (pointer == 0) throw new InvalidOperationException("clipboard_lock_failed");
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { Native.GlobalUnlock(memory); }
            if (Native.SetClipboardData(13, memory) == 0) throw new InvalidOperationException("clipboard_set_failed");
            memory = 0; // Windows now owns the allocation.
        }
        catch (Exception) { error = "clipboard_write_failed"; }
        finally { if (memory != 0) Native.GlobalFree(memory); if (opened) Native.CloseClipboard(); }
        var after = Native.GetClipboardSequenceNumber();
        return new(error is null && after != before, before, after, Math.Min(attempts, 10),
            error ?? (after == before ? "clipboard_sequence_unchanged" : null));
    }
    public void Dispose() => owner.Dispose();
    private static class Native
    {
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] public static extern bool OpenClipboard(nint owner);
        [DllImport("user32.dll")] public static extern bool EmptyClipboard();
        [DllImport("user32.dll")] public static extern bool CloseClipboard();
        [DllImport("user32.dll")] public static extern nint SetClipboardData(uint format, nint memory);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern nint GlobalAlloc(uint flags, nuint bytes);
        [DllImport("kernel32.dll")] public static extern nint GlobalLock(nint memory);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(nint memory);
        [DllImport("kernel32.dll")] public static extern nint GlobalFree(nint memory);
    }
}
