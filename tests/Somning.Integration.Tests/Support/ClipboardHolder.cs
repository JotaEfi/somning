using System.Runtime.InteropServices;
using Somning.Platform.Windows.Clipboard;
namespace Somning.Integration.Tests.Support;
public sealed class ClipboardHolder : IDisposable
{
    private readonly MessageOnlyWindow owner = new();
    private bool opened;
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    public ClipboardHolder()
    {
        opened = owner.Invoke(() =>
        {
            for (int attempt = 0; attempt < 20; attempt++)
            { if (OpenClipboard(owner.Handle)) return true; Thread.Sleep(20); }
            return false;
        });
        if (!opened) { owner.Dispose(); throw new IOException("Unable to hold clipboard."); }
    }
    public void Dispose()
    {
        try { if (opened) { owner.Invoke(CloseClipboard); opened = false; } }
        finally { owner.Dispose(); }
    }
}
