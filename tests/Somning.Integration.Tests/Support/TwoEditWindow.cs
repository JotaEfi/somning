using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace Somning.Integration.Tests.Support;

public sealed class TwoEditWindow : IDisposable
{
    private const uint WmClose = 0x0010, WmDestroy = 0x0002, WmAppFocusEdit = 0x8001;
    private const uint WsVisible = 0x10000000, WsOverlappedWindow = 0x00CF0000, WsChild = 0x40000000, WsTabstop = 0x00010000;
    private static readonly string ClassName = $"Somning.TwoEditWindow.{Environment.ProcessId}";
    private static readonly WndProcDelegate WindowProcedure = WndProc;
    private static readonly ConcurrentDictionary<uint, TwoEditWindow> Instances = new();
    private static int registered;
    private readonly Thread thread;
    private readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource loopEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TwoEditWindow()
    {
        thread = new Thread(WindowThread) { IsBackground = true, Name = "Somning test edit window" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Window = ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    public nint Window { get; }
    public nint EditHandle(int index)
    {
        if ((uint)index >= (uint)Edits.Length) throw new ArgumentOutOfRangeException(nameof(index));
        return Edits[index];
    }
    private nint[] Edits { get; set; } = [];

    public static TwoEditWindow Start() => new();

    public void FocusEdit(int index)
    {
        if ((uint)index >= (uint)Edits.Length) throw new ArgumentOutOfRangeException(nameof(index));
        _ = SendMessage(Window, WmAppFocusEdit, (nint)index, 0);
    }

    /// <summary>
    /// Polls until the requested edit is the focused control AND this window is the foreground window.
    /// FocusEdit marshals to the window thread, so returning from it does not mean focus has moved.
    /// </summary>
    public bool WaitUntilFocused(int index, TimeSpan timeout)
    {
        var expected = EditHandle(index);
        var until = DateTime.UtcNow + timeout;
        do
        {
            if (ForegroundHelper.FocusedControl(Window) == expected && ForegroundHelper.ForegroundWindow() == Window)
                return true;
            Thread.Sleep(25);
        } while (DateTime.UtcNow < until);
        return false;
    }

    public string GetText(int index)
    {
        if ((uint)index >= (uint)Edits.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var edit = Edits[index];
        var length = SendMessage(edit, 0x000E, 0, 0).ToInt32();
        var text = new StringBuilder(Math.Max(length + 1, 1));
        _ = SendMessageText(edit, 0x000D, (nint)text.Capacity, text);
        return text.ToString();
    }

    private void WindowThread()
    {
        var threadId = GetCurrentThreadId();
        nint window = 0;
        Instances[threadId] = this;
        try
        {
            RegisterWindowClass();
            window = CreateWindowEx(0, ClassName, "Somning paste test", WsOverlappedWindow | WsVisible,
                100, 100, 500, 260, 0, 0, GetModuleHandle(null), 0);
            if (window == 0) throw new InvalidOperationException("Could not create two-edit test window.");
            Edits =
            [
                CreateWindowEx(0, "EDIT", "", WsChild | WsVisible | WsTabstop | 0x1000, 12, 12, 450, 70, window, 0, GetModuleHandle(null), 0),
                CreateWindowEx(0, "EDIT", "", WsChild | WsVisible | WsTabstop | 0x1000, 12, 100, 450, 70, window, 0, GetModuleHandle(null), 0)
            ];
            if (Edits.Any(edit => edit == 0)) throw new InvalidOperationException("Could not create test edit controls.");
            _ = SetFocus(Edits[0]);
            ready.TrySetResult(window);
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                _ = TranslateMessage(ref message);
                _ = DispatchMessage(ref message);
            }
        }
        catch (Exception error) { ready.TrySetException(error); }
        finally
        {
            if (window != 0) _ = DestroyWindow(window);
            Instances.TryRemove(threadId, out _);
            loopEnded.TrySetResult();
        }
    }

    private static void RegisterWindowClass()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        var windowClass = new WindowClass
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProcedure),
            hInstance = GetModuleHandle(null),
            lpszClassName = ClassName
        };
        if (RegisterClass(ref windowClass) == 0 && Marshal.GetLastWin32Error() != 1410)
        {
            Interlocked.Exchange(ref registered, 0);
            throw new InvalidOperationException("Could not register two-edit test window class.");
        }
    }

    private static nint WndProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WmAppFocusEdit && Instances.TryGetValue(GetCurrentThreadId(), out var instance)
            && (nuint)wParam < (nuint)instance.Edits.Length)
            _ = SetFocus(instance.Edits[(int)wParam]);
        if (message == WmDestroy) PostQuitMessage(0);
        return DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (thread.IsAlive) _ = PostMessage(Window, WmClose, 0, 0);
        if (!loopEnded.Task.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test window thread did not stop.");
        thread.Join();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WndProcDelegate(nint window, uint message, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WindowClass
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public int ptX, ptY;
        public uint lPrivate;
    }

    [DllImport("kernel32.dll")] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y,
        int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] private static extern int GetMessage(out Message message, nint window, uint min, uint max);
    [DllImport("user32.dll", EntryPoint = "TranslateMessage")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "PostQuitMessage")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll", EntryPoint = "DestroyWindow")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "SetFocus")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern nint SendMessageText(nint window, uint message, nint wParam, StringBuilder text);
}
