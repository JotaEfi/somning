using System.Collections.Concurrent;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Somning.Platform.Windows.Input;
using PInvoke = Windows.Win32.IntegrationPInvoke;
namespace Somning.Integration.Tests.Support;
public sealed unsafe class ObserverHook : IDisposable
{
    private readonly HOOKPROC callback;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private uint threadId;
    public ConcurrentQueue<(uint Key, bool Down)> Events { get; } = new();
    public ObserverHook()
    {
        callback = Observe;
        thread = new Thread(Run) { IsBackground = true, Name = "test-observer-hook" };
        thread.Start(); ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }
    private void Run()
    {
        try
        {
            threadId = PInvoke.GetCurrentThreadId();
            PInvoke.PeekMessage(out _, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
            using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, callback, null, 0);
            if (hook.IsInvalid) throw new InvalidOperationException("Observer hook failed.");
            ready.TrySetResult();
            while ((int)PInvoke.GetMessage(out var message, default, 0, 0) > 0)
            { PInvoke.TranslateMessage(in message); PInvoke.DispatchMessage(in message); }
        }
        catch (Exception ex) { ready.TrySetException(ex); }
    }
    private LRESULT Observe(int code, WPARAM wparam, LPARAM lparam)
    {
        if (code >= 0)
        {
            var data = (KBDLLHOOKSTRUCT*)lparam.Value;
            if (data->dwExtraInfo is InputMarkers.Test or InputMarkers.Paste)
                Events.Enqueue((data->vkCode, wparam.Value is 0x100 or 0x104));
        }
        return PInvoke.CallNextHookEx((HHOOK)default, code, wparam, lparam);
    }
    public void Dispose()
    {
        PInvoke.PostThreadMessage(threadId, PInvoke.WM_QUIT, default, default);
        thread.Join(TimeSpan.FromSeconds(2)); GC.KeepAlive(callback);
    }
}
