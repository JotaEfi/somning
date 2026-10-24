using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Somning.Platform.Windows.Power;

public sealed class PowerSessionListener : IDisposable
{
    private const uint StopMessage = 0x8000 + 0x34;
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private readonly WindowProcedure procedure;
    private uint threadId;
    private nint window;
    private int disposed;

    public event Action? Suspended;
    public event Action? Resumed;
    public event Action? SessionLocked;
    public event Action? SessionUnlocked;

    internal nint Handle => window;

    public PowerSessionListener()
    {
        procedure = WindowProc;
        thread = new Thread(Run) { IsBackground = true, Name = "Somning.PowerSession" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); }
        catch { Dispose(); throw; }
    }

    private void Run()
    {
        var className = "SomningPowerSession-" + Guid.NewGuid().ToString("N");
        var instance = GetModuleHandle(null);
        var cls = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = instance,
            Procedure = Marshal.GetFunctionPointerForDelegate(procedure), ClassName = className
        };
        bool registeredClass = false;
        bool registeredSession = false;
        try
        {
            threadId = GetCurrentThreadId();
            PeekMessage(out _, 0, 0, 0, 0); // Create the queue before Dispose can post to it.
            if (RegisterClassEx(in cls) == 0) throw new Win32Exception();
            registeredClass = true;
            // A hidden top-level window receives power broadcasts; HWND_MESSAGE does not.
            window = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, 0, 0, instance, 0);
            if (window == 0) throw new Win32Exception();
            if (!WTSRegisterSessionNotification(window, 0)) throw new Win32Exception();
            registeredSession = true;
            ready.TrySetResult();
            int status;
            while (Volatile.Read(ref disposed) == 0 && (status = GetMessage(out var message, 0, 0, 0)) != 0)
            {
                if (status == -1) throw new Win32Exception();
                if (message.Id == StopMessage) break;
                TranslateMessage(in message);
                DispatchMessage(in message);
            }
        }
        catch (Exception ex)
        {
            ready.TrySetException(ex);
            Trace.TraceError("Power/session listener failed: {0}", ex);
        }
        finally
        {
            if (registeredSession && !WTSUnRegisterSessionNotification(window))
                Trace.TraceWarning("WTSUnRegisterSessionNotification failed: {0}", Marshal.GetLastWin32Error());
            if (window != 0 && !DestroyWindow(window))
                Trace.TraceWarning("DestroyWindow for power/session listener failed: {0}", Marshal.GetLastWin32Error());
            window = 0;
            if (registeredClass && !UnregisterClass(className, instance))
                Trace.TraceWarning("UnregisterClass for power/session listener failed: {0}", Marshal.GetLastWin32Error());
        }
    }

    private nint WindowProc(nint hwnd, uint message, nuint wparam, nint lparam)
    {
        Action? handlers = message switch
        {
            0x0218 => wparam switch { 0x0004 => Suspended, 0x0012 or 0x0007 => Resumed, _ => null },
            0x02B1 => wparam switch { 0x7 => SessionLocked, 0x8 => SessionUnlocked, _ => null },
            _ => null
        };
        if (Volatile.Read(ref disposed) == 0 && handlers is not null)
        {
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception ex) { Trace.TraceError("Power/session event handler failed: {0}", ex); }
            }
        }
        if (message == 0x0218) return 1;
        return DefWindowProc(hwnd, message, wparam, lparam);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (threadId != 0 && !PostThreadMessage(threadId, StopMessage, 0, 0) && thread.IsAlive)
            Trace.TraceWarning("Unable to signal power/session listener shutdown: {0}", Marshal.GetLastWin32Error());
        if (Thread.CurrentThread != thread && !thread.Join(TimeSpan.FromSeconds(5)))
            Trace.TraceWarning("Power/session listener shutdown is still pending.");
        GC.KeepAlive(procedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nuint wparam, nint lparam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName, ClassName;
        public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(in WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint hwnd, uint msg, nuint wparam, nint lparam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool PostThreadMessage(uint id, uint msg, nuint wparam, nint lparam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetMessage(out Message msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PeekMessage(out Message msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(in Message msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DispatchMessage(in Message msg);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSUnRegisterSessionNotification(nint hwnd);
}
