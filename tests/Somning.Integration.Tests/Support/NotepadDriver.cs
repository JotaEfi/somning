using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Somning.Integration.Tests.Support;

public sealed class NotepadDriver : IDisposable
{
    private const uint WmGetText = 0x000D;
    private const uint WmGetTextLength = 0x000E;
    private const uint WmSetText = 0x000C;
    private readonly Process process;

    private NotepadDriver(Process process, nint window, nint edit)
    {
        this.process = process;
        Window = window;
        Edit = edit;
    }

    public nint Window { get; }
    public nint Edit { get; }

    public static NotepadDriver Start()
    {
        var process = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true })
            ?? throw new InvalidOperationException("Could not start Notepad.");
        try
        {
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            nint window = 0, edit = 0;
            while (DateTime.UtcNow < until && !process.HasExited)
            {
                process.Refresh();
                window = process.MainWindowHandle;
                if (window != 0)
                {
                    edit = FindWindowEx(window, 0, "Edit", null);
                    if (edit == 0) edit = FindWindowEx(window, 0, "RichEditD2DPT", null);
                    if (edit != 0) return new(process, window, edit);
                }
                Thread.Sleep(50);
            }
            throw new TimeoutException("Notepad edit control did not appear.");
        }
        catch { process.Dispose(); throw; }
    }

    public string GetText()
    {
        var length = SendMessage(Edit, WmGetTextLength, 0, 0).ToInt32();
        var buffer = new StringBuilder(Math.Max(length + 1, 1));
        _ = SendMessage(Edit, WmGetText, (nint)buffer.Capacity, buffer);
        return buffer.ToString();
    }

    public string WaitForText(string expected, TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(2));
        var actual = GetText();
        while (actual != expected && DateTime.UtcNow < until)
        {
            Thread.Sleep(20);
            actual = GetText();
        }
        return actual;
    }

    public void SetText(string text) => _ = SendMessage(Edit, WmSetText, 0, text);

    public void Dispose()
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        process.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint child, string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, StringBuilder text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, string text);
}
