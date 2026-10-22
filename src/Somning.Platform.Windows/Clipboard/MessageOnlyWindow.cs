using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace Somning.Platform.Windows.Clipboard;
public sealed class MessageOnlyWindow : IDisposable
{
    private const uint Dispatch = 0x802A;
    private readonly ConcurrentQueue<Action> actions = new();
    private readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private readonly WindowProcedure procedure;
    private int disposed;
    public nint Handle { get; }
    public MessageOnlyWindow()
    {
        procedure = WindowProc;
        thread = new Thread(Run) { IsBackground = true, Name = "somning-clipboard-STA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Handle = ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }
    public T Invoke<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Thread.CurrentThread == thread) return action();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        actions.Enqueue(() => { try { completion.SetResult(action()); } catch (Exception ex) { completion.SetException(ex); } });
        if (!Native.PostMessage(Handle, Dispatch, 0, 0)) throw new Win32Exception();
        return completion.Task.GetAwaiter().GetResult();
    }
    private void Run()
    {
        var className = "SomningClipboard-" + Guid.NewGuid().ToString("N");
        var instance = Native.GetModuleHandle(null);
        var cls = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = instance,
            Procedure = Marshal.GetFunctionPointerForDelegate(procedure), ClassName = className };
        bool registered = false;
        try
        {
            if (Native.RegisterClassEx(in cls) == 0) throw new Win32Exception();
            registered = true;
            var hwnd = Native.CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, -3, 0, instance, 0);
            if (hwnd == 0) throw new Win32Exception();
            ready.TrySetResult(hwnd);
            int status;
            while ((status = Native.GetMessage(out var message, 0, 0, 0)) > 0)
            { Native.TranslateMessage(in message); Native.DispatchMessage(in message); }
            if (status == -1) throw new Win32Exception();
        }
        catch (Exception ex) { ready.TrySetException(ex); }
        finally { if (registered) Native.UnregisterClass(className, instance); }
    }
    private nint WindowProc(nint hwnd, uint message, nuint wparam, nint lparam)
    {
        if (message == Dispatch) { while (actions.TryDequeue(out var action)) action(); return 0; }
        if (message == 0x10) { Native.DestroyWindow(hwnd); return 0; }
        if (message == 2) { Native.PostQuitMessage(0); return 0; }
        return Native.DefWindowProc(hwnd, message, wparam, lparam);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Native.PostMessage(Handle, 0x10, 0, 0);
        if (Thread.CurrentThread != thread) thread.Join(TimeSpan.FromSeconds(5));
        GC.KeepAlive(procedure);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nuint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style; public nint Procedure; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName, ClassName; public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window; public uint Id; public nuint WParam; public nint LParam;
        public uint Time; public int X, Y; public uint Private;
    }
    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassEx(in WindowClass cls);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool UnregisterClass(string name, nint instance);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint DefWindowProc(nint hwnd, uint msg, nuint wparam, nint lparam);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool PostMessage(nint hwnd, uint msg, nuint wparam, nint lparam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetMessage(out Message msg, nint hwnd, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(in Message msg);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint DispatchMessage(in Message msg);
    }
}
