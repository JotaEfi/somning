using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Somning.Core.Output;
using Somning.Core.Ports;
using Somning.Platform.Windows.Clipboard;
using Somning.Platform.Windows.Paste;

namespace Somning.Bench;

internal sealed class OwnedEditor : IDisposable
{
    private readonly Form form;
    private readonly TextBox textBox;
    private readonly nint hDesk;

    private OwnedEditor(Form form, TextBox box, nint hDesk)
    {
        this.form = form;
        textBox = box;
        this.hDesk = hDesk;
    }

    public static async Task<OwnedEditor> StartAsync()
    {
        var hDesk = Native.OpenInputDesktop(0, false, 0x01FF);
        if (hDesk != 0) Native.SetThreadDesktop(hDesk);

        var ready = new TaskCompletionSource<OwnedEditor>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            if (hDesk != 0) Native.SetThreadDesktop(hDesk);
            var form = new Form
            {
                Text = "Somning Bench Owned Editor",
                Width = 600,
                Height = 240,
                TopMost = true,
                StartPosition = FormStartPosition.CenterScreen
            };
            var box = new TextBox { Multiline = true, Dock = DockStyle.Fill };
            form.Controls.Add(box);
            form.Shown += (_, _) => ready.TrySetResult(new OwnedEditor(form, box, hDesk));
            Application.Run(form);
        })
        {
            IsBackground = true,
            Name = "bench-editor"
        };
        // NOTE: Do not pre-set STA before start; CLR would initialize COM OLE window on default desktop, causing SetThreadDesktop to fail with ERROR_BUSY.
        thread.Start();

        var editor = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        editor.ClearAndActivate();
        return editor;
    }

    public void ClearAndActivate()
    {
        if (hDesk != 0) Native.SetThreadDesktop(hDesk);

        form.Invoke(() =>
        {
            textBox.Clear();
            form.Show();
            form.WindowState = FormWindowState.Normal;
            form.Activate();
            textBox.Focus();
            Native.SetFocus(textBox.Handle);
            Native.BringWindowToTop(form.Handle);
            Native.SetForegroundWindow(form.Handle);
        });

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 1000)
        {
            if (Native.GetForegroundWindow() == form.Handle) break;
            Thread.Sleep(25);
        }
    }

    public bool HasExpectedText(string expected) => (bool)form.Invoke(() => textBox.Text == expected);

    public bool WaitForText(string expected, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (HasExpectedText(expected)) return true;
            Thread.Sleep(25);
        }
        return HasExpectedText(expected);
    }

    public string GetText() => (string)form.Invoke(() => textBox.Text);

    public Task<DeliveryResult> DeliverAsync(string text, PasteTarget target, CancellationToken ct)
    {
        if (hDesk != 0) Native.SetThreadDesktop(hDesk);
        using var clipboard = new ClipboardWriter();
        var paster = new TargetPaster(
            clipboard,
            new ForegroundProbe(new IntegrityReader()),
            new IntegrityReader(),
            new ModifierWaiter(),
            new PasteInjector());
        return paster.DeliverAsync(text, target, ct);
    }

    public void Dispose()
    {
        try { form.Invoke(form.Close); } catch { }
        if (hDesk != 0) { try { Native.CloseDesktop(hDesk); } catch { } }
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)] public static extern nint OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetThreadDesktop(nint hDesktop);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseDesktop(nint hDesktop);
        [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(nint hwnd);
        [DllImport("user32.dll")] public static extern nint SetFocus(nint hwnd);
    }
}
