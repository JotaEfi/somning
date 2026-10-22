using System.Threading.Channels;
using Somning.Core.Trigger;
using Somning.Platform.Windows.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
namespace Somning.Platform.Windows.Hook;
public sealed unsafe class KeyboardHook(ChannelWriter<KeyEvent> sink) : IDisposable
{
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? thread;
    private uint threadId;
    private ChordTracker tracker;
    private int disposed;
    private HOOKPROC? rootedCallback;
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (thread is not null) throw new InvalidOperationException("Hook already started.");
        rootedCallback = OnKeyboard;
        thread = new Thread(Run) { Name = "Somning.Hook", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        thread.Start();
        ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }
    private void Run()
    {
        try
        {
            threadId = PInvoke.GetCurrentThreadId();
            PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
            using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, rootedCallback!, null, 0);
            if (hook.IsInvalid) throw new InvalidOperationException("Unable to install keyboard hook.");
            ready.TrySetResult();
            while ((int)PInvoke.GetMessage(out var message, default, 0, 0) > 0)
            { PInvoke.TranslateMessage(in message); PInvoke.DispatchMessage(in message); }
        }
        catch (Exception ex) { ready.TrySetException(ex); }
        finally { sink.TryComplete(); }
    }
    private LRESULT OnKeyboard(int code, WPARAM wparam, LPARAM lparam)
    {
        if (code < 0) return PInvoke.CallNextHookEx((HHOOK)default, code, wparam, lparam);
        var data = (KBDLLHOOKSTRUCT*)lparam.Value;
        if (data->dwExtraInfo == InputMarkers.Paste)
            return PInvoke.CallNextHookEx((HHOOK)default, code, wparam, lparam);
        bool down = wparam.Value is 0x100 or 0x104;
        bool up = wparam.Value is 0x101 or 0x105;
        if ((down || up) && tracker.Process(data->vkCode, down, out var kind))
        {
            sink.TryWrite(new KeyEvent(kind, Environment.TickCount64, data->dwExtraInfo == InputMarkers.Test));
            return new LRESULT(1);
        }
        return PInvoke.CallNextHookEx((HHOOK)default, code, wparam, lparam);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (thread is not null && threadId != 0)
        {
            PInvoke.PostThreadMessage(threadId, PInvoke.WM_QUIT, default, default);
            if (Thread.CurrentThread != thread) thread.Join(TimeSpan.FromSeconds(2));
        }
        GC.KeepAlive(rootedCallback);
    }
}
