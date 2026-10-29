using System.Threading.Channels;
using System.Runtime.InteropServices;
using Somning.Core.Trigger;
using Somning.Platform.Windows.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Somning.Platform.Windows.Hook;

public sealed unsafe class KeyboardHook(ChannelWriter<KeyEvent> sink) : IDisposable
{
    private const uint LayoutRestoreMessage = 0x8000 + 0x31;
    private const uint LayoutVerifyMessage = 0x8000 + 0x32;
    private const uint ReinstallHookMessage = 0x8000 + 0x33;
    private const uint SilentRemovalMessage = 0x8000 + 0x34;
    private int probeSeenCount;
    private int hookActive;
    private nint hookHandle;
    private int installCount;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? thread;
    private uint threadId;
    private ChordTracker tracker;
    private uint targetLayoutThreadId;
    private nint targetLayoutWindow;
    private nint layoutBeforeChord;
    private int disposed;
    private HOOKPROC? rootedCallback;

    /// <summary>HOTK-04: Predicate returning whether dictation recording is actively in progress.</summary>
    public Func<bool>? IsRecordingActive { get; set; }

    // Indicates local installation state; Windows can silently remove a low-level hook.
    public bool IsHookActive => Volatile.Read(ref hookActive) != 0;

    /// <summary>Test observability: native handle of the currently installed hook (0 before Start).</summary>
    internal nint HookHandle => Volatile.Read(ref hookHandle);

    /// <summary>Test observability: number of successful hook installations, including reinstalls.</summary>
    public int InstallCount => Volatile.Read(ref installCount);

    /// <summary>Liveness observability: number of marker probe keystrokes the hook callback has swallowed.</summary>
    public int ProbeSeenCount => Volatile.Read(ref probeSeenCount);

    /// <summary>
    /// Test seam: unhooks the native hook on the hook thread WITHOUT updating hookActive/installCount, imitating
    /// Windows silently dropping a low-level hook after a callback timeout.
    /// </summary>
    internal void SimulateSilentRemovalForTests()
    {
        if (!ready.Task.IsCompletedSuccessfully) throw new InvalidOperationException("Hook has not started.");
        if (!PInvoke.PostThreadMessage(threadId, SilentRemovalMessage, default, default))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Reinstall()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (!ready.Task.IsCompletedSuccessfully)
            throw new InvalidOperationException("Hook has not started.");
        if (!PInvoke.PostThreadMessage(threadId, ReinstallHookMessage, default, default))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

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
            var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, rootedCallback!, null, 0);
            try
            {
                if (hook.IsInvalid) throw new InvalidOperationException("Unable to install keyboard hook.");
                hookHandle = hook.DangerousGetHandle();
                _ = Interlocked.Increment(ref installCount);
                Volatile.Write(ref hookActive, 1);
                ready.TrySetResult();
                while ((int)PInvoke.GetMessage(out var message, default, 0, 0) > 0)
                {
                    if ((uint)message.message == ReinstallHookMessage)
                    {
                        Volatile.Write(ref hookActive, 0);
                        DisposeHookTolerant(hook); // SafeHandle releases via UnhookWindowsHookEx on this thread.
                        tracker = default;
                        hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, rootedCallback!, null, 0);
                        if (hook.IsInvalid) throw new InvalidOperationException("Unable to reinstall keyboard hook.");
                        hookHandle = hook.DangerousGetHandle();
                        _ = Interlocked.Increment(ref installCount);
                        Volatile.Write(ref hookActive, 1);
                    }
                    else if ((uint)message.message == SilentRemovalMessage)
                    {
                        _ = PInvoke.UnhookWindowsHookEx((HHOOK)hookHandle);
                    }
                    else if ((uint)message.message == LayoutRestoreMessage) QueueLayoutVerification();
                    else if ((uint)message.message == LayoutVerifyMessage) RestoreHotkeyLayout();
                    else { PInvoke.TranslateMessage(in message); PInvoke.DispatchMessage(in message); }
                }
            }
            finally { Volatile.Write(ref hookActive, 0); DisposeHookTolerant(hook); }
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
            sink.TryComplete(ex);
            System.Diagnostics.Trace.TraceError("Keyboard hook thread failed: {0}", ex);
        }
        finally { sink.TryComplete(); }
    }

    /// <summary>Disposing a hook Windows already removed makes the native unhook fail; that must not throw.</summary>
    private static void DisposeHookTolerant(IDisposable hook)
    {
        try { hook.Dispose(); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Hook dispose after removal: {0}", ex.Message); }
    }

    private LRESULT OnKeyboard(int code, WPARAM wparam, LPARAM lparam)
    {
        if (code < 0) return PInvoke.CallNextHookEx((HHOOK)default, code, wparam, lparam);
        var data = (KBDLLHOOKSTRUCT*)lparam.Value;
        if (data->dwExtraInfo == InputMarkers.Probe)
        {
            _ = Interlocked.Increment(ref probeSeenCount);
            return new LRESULT(1); // swallow: the target app never sees the liveness probe key
        }
        if (data->dwExtraInfo == InputMarkers.Paste || data->dwExtraInfo == InputMarkers.Mask)
            return PInvoke.CallNextHookEx((HHOOK)default, code, wparam, lparam);
        bool down = wparam.Value is 0x100 or 0x104;
        bool up = wparam.Value is 0x101 or 0x105;
        if ((down || up) && tracker.Process(data->vkCode, down, out var kind))
        {
            if (kind == KeyEventKind.TriggerDown)
            {
                var foreground = GetForegroundWindow();
                targetLayoutWindow = foreground;
                targetLayoutThreadId = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);
                layoutBeforeChord = targetLayoutThreadId == 0 ? 0 : GetKeyboardLayout(targetLayoutThreadId);
            }
            sink.TryWrite(new KeyEvent(kind, Environment.TickCount64, data->dwExtraInfo == InputMarkers.Test));
            if (kind == KeyEventKind.TriggerUp)
                _ = PInvoke.PostThreadMessage(threadId, LayoutRestoreMessage, default, default);
            return new LRESULT(1);
        }

        // HOTK-04: Pressing Esc (VK_ESCAPE = 0x1B) while recording cancels it immediately
        if (down && data->vkCode == 0x1B && IsRecordingActive?.Invoke() == true)
        {
            sink.TryWrite(new KeyEvent(KeyEventKind.Cancel, Environment.TickCount64, data->dwExtraInfo == InputMarkers.Test));
            return new LRESULT(1);
        }

        return PInvoke.CallNextHookEx((HHOOK)default, code, wparam, lparam);
    }

    /// <summary>
    /// Ctrl+Shift+Space can activate Windows' Ctrl+Shift input-language shortcut on key release. Delay the check
    /// outside the hook thread so all remaining key-up callbacks drain first, then post a request for the captured
    /// HKL to the original foreground window. This restores the exact layout even when more than two are installed.
    /// </summary>
    private void QueueLayoutVerification()
    {
        var hookThread = threadId;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(100);
            _ = PInvoke.PostThreadMessage(hookThread, LayoutVerifyMessage, default, default);
        });
    }

    private void RestoreHotkeyLayout()
    {
        if (targetLayoutWindow == 0 || targetLayoutThreadId == 0 || layoutBeforeChord == 0 ||
            GetKeyboardLayout(targetLayoutThreadId) == layoutBeforeChord) return;
        // Explicitly request the saved HKL. Replaying Ctrl+Shift only reverses a two-layout cycle and can
        // advance to a third layout on machines with more than two installed layouts.
        _ = PostMessage(targetLayoutWindow, WmInputLangChangeRequest, 0, layoutBeforeChord);
    }

    private const uint WmInputLangChangeRequest = 0x0050;
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

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
