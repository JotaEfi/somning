using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Somning.Platform.Windows.Tray;

public sealed class TrayIconManager : IDisposable
{
    private const uint WmTrayCallback = 0x8000 + 0x42;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const int WmRbuttonUp = 0x0205;
    private const int WmLbuttonUp = 0x0202;
    private const int WmContextMenu = 0x007B;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfGrayed = 0x00000001;
    private const uint MfEnabled = 0x00000000;

    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const int MenuSettings = 1001;
    private const int MenuRepaste = 1002;
    private const int MenuOpenLogs = 1003;
    private const int MenuExit = 1004;

    private readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private readonly WindowProcedure procedure;
    private nint hwnd;
    private nint defaultIcon;
    private int disposed;

    public event Action? OpenSettingsRequested;
    public event Action? RepasteLastRequested;
    public event Action? OpenLogsRequested;
    public event Action? ExitRequested;

    public Func<bool>? CanRepaste { get; set; }

    public TrayIconManager(string initialTooltip = "Somning - Ditado por Voz")
    {
        procedure = WindowProc;
        thread = new Thread(Run) { IsBackground = true, Name = "Somning.Tray" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        hwnd = ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        defaultIcon = LoadDefaultIcon();
        AddTrayIcon(initialTooltip);
    }

    public void UpdateTooltip(string tooltip)
    {
        if (Volatile.Read(ref disposed) != 0 || hwnd == 0) return;
        var data = CreateNotifyData();
        data.uFlags = NifTip;
        data.szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        _ = Shell_NotifyIcon(NimModify, ref data);
    }

    private void AddTrayIcon(string tooltip)
    {
        var data = CreateNotifyData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = WmTrayCallback;
        data.hIcon = defaultIcon;
        data.szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (!Shell_NotifyIcon(NimAdd, ref data))
            Trace.TraceWarning("Shell_NotifyIcon(NIM_ADD) returned false.");
    }

    private void RemoveTrayIcon()
    {
        if (hwnd == 0) return;
        var data = CreateNotifyData();
        _ = Shell_NotifyIcon(NimDelete, ref data);
    }

    private NOTIFYICONDATA CreateNotifyData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1
        };
    }

    private void Run()
    {
        var className = "SomningTray-" + Guid.NewGuid().ToString("N");
        var instance = GetModuleHandle(null);
        var cls = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Instance = instance,
            Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
            ClassName = className
        };

        if (RegisterClassEx(in cls) == 0)
        {
            ready.TrySetException(new Win32Exception());
            return;
        }

        var createdHwnd = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, -3, 0, instance, 0);
        if (createdHwnd == 0)
        {
            ready.TrySetException(new Win32Exception());
            return;
        }

        ready.TrySetResult(createdHwnd);

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(in msg);
            DispatchMessage(in msg);
        }
    }

    private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WmTrayCallback)
        {
            var mouseEvent = (int)lParam;
            if (mouseEvent is WmRbuttonUp or WmLbuttonUp or WmContextMenu)
            {
                ShowContextMenu();
                return 0;
            }
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;

        try
        {
            var repasteFlags = (CanRepaste?.Invoke() ?? false) ? MfString | MfEnabled : MfString | MfGrayed;

            AppendMenuW(menu, MfString, MenuSettings, "Configurações...");
            AppendMenuW(menu, repasteFlags, MenuRepaste, "Colar último");
            AppendMenuW(menu, MfString, MenuOpenLogs, "Abrir pasta de logs");
            AppendMenuW(menu, MfSeparator, 0, string.Empty);
            AppendMenuW(menu, MfString, MenuExit, "Sair");

            GetCursorPos(out var pt);
            SetForegroundWindow(hwnd);

            var cmd = TrackPopupMenuEx(menu, TpmRightButton | TpmReturnCmd, pt.X, pt.Y, hwnd, 0);
            PostMessageW(hwnd, 0, 0, 0); // dismiss hack

            switch (cmd)
            {
                case MenuSettings:
                    OpenSettingsRequested?.Invoke();
                    break;
                case MenuRepaste:
                    RepasteLastRequested?.Invoke();
                    break;
                case MenuOpenLogs:
                    OpenLogsRequested?.Invoke();
                    break;
                case MenuExit:
                    ExitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static nint LoadDefaultIcon()
    {
        // IDI_APPLICATION = 32512
        return LoadIconW(0, 32512);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        RemoveTrayIcon();
        if (hwnd != 0)
        {
            PostMessageW(hwnd, 0x0010 /* WM_CLOSE */, 0, 0);
            PostMessageW(hwnd, 0x0012 /* WM_QUIT */, 0, 0);
            if (Thread.CurrentThread != thread)
                thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Hwnd;
        public uint Msg;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public POINT Point;
    }

    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(nint hMenu, uint uFlags, nint uIDNewItem, string lpNewItem);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint hMenu);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern nint LoadIconW(nint hInstance, int lpIconName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(in WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out Message message, nint window, uint filterMin, uint filterMax);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(in Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(in Message message);
}
