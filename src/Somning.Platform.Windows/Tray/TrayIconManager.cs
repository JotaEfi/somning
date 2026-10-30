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
    private bool ownsDefaultIcon;
    private int disposed;
    private readonly string initialTooltip;
    private uint taskbarCreatedMessage;
    private volatile bool isRegistered;
    public bool IsRegistered => isRegistered;

    public event Action? OpenSettingsRequested;
    public event Action? RepasteLastRequested;
    public event Action? OpenLogsRequested;
    public event Action? ExitRequested;

    public Func<bool>? CanRepaste { get; set; }

    public TrayIconManager(string initialTooltip = "Somning - Ditado por Voz")
    {
        this.initialTooltip = initialTooltip;
        procedure = WindowProc;
        thread = new Thread(Run) { IsBackground = true, Name = "Somning.Tray" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        hwnd = ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    public void UpdateTooltip(string tooltip)
    {
        if (Volatile.Read(ref disposed) != 0 || hwnd == 0) return;
        var data = CreateNotifyData();
        data.uFlags = NifTip;
        data.szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        _ = Shell_NotifyIcon(NimModify, ref data);
    }

    internal static bool RegisterWithRetry(Func<bool> register, Action wait)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (register()) return true;
            if (attempt < 4) wait();
        }
        return false;
    }

    private bool AddTrayIcon(string tooltip)
    {
        var data = CreateNotifyData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        data.uCallbackMessage = WmTrayCallback;
        data.hIcon = defaultIcon;
        data.szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        isRegistered = RegisterWithRetry(() => Shell_NotifyIcon(NimAdd, ref data), () => Thread.Sleep(250));
        return isRegistered;
    }

    private void RemoveTrayIcon()
    {
        if (hwnd == 0) return;
        var data = CreateNotifyData();
        _ = Shell_NotifyIcon(NimDelete, ref data);
        isRegistered = false;
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

        var createdHwnd = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, 0, 0, instance, 0);
        if (createdHwnd == 0)
        {
            ready.TrySetException(new Win32Exception());
            return;
        }

        hwnd = createdHwnd;
        try
        {
            taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
            defaultIcon = LoadDefaultIcon();
            if (!AddTrayIcon(initialTooltip))
            {
                ready.TrySetException(new InvalidOperationException("Windows refused the Somning tray icon after five attempts."));
                return;
            }
            ready.TrySetResult(createdHwnd);
            while (GetMessage(out var msg, 0, 0, 0) > 0)
            {
                TranslateMessage(in msg);
                DispatchMessage(in msg);
            }
        }
        catch (Exception ex) { ready.TrySetException(ex); }
        finally
        {
            RemoveTrayIcon();
            if (ownsDefaultIcon && defaultIcon != 0) DestroyIcon(defaultIcon);
            defaultIcon = 0;
            ownsDefaultIcon = false;
            DestroyWindow(createdHwnd);
            hwnd = 0;
            UnregisterClassW(className, instance);
        }
    }
    private nint WindowProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (taskbarCreatedMessage != 0 && message == taskbarCreatedMessage)
        {
            if (!AddTrayIcon(initialTooltip)) Trace.TraceWarning("Could not restore Somning tray icon after Explorer restart.");
            return 0;
        }
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
        var popupThread = new Thread(() =>
        {
            var menu = new System.Windows.Controls.ContextMenu
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 28, 33)),
                Foreground = System.Windows.Media.Brushes.WhiteSmoke,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 78, 86)),
                BorderThickness = new System.Windows.Thickness(1),
                Padding = new System.Windows.Thickness(6),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 13,
                MinWidth = 220,
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };
            var menuStyle = (System.Windows.Style)System.Windows.Markup.XamlReader.Parse("""
                <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu">
                  <Setter Property="OverridesDefaultStyle" Value="True"/>
                  <Setter Property="Template"><Setter.Value>
                    <ControlTemplate TargetType="ContextMenu">
                      <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                              BorderThickness="1" CornerRadius="10" Padding="6" SnapsToDevicePixels="True">
                        <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
                      </Border>
                    </ControlTemplate>
                  </Setter.Value></Setter>
                </Style>
                """);
            menu.Style = menuStyle;
            menu.HasDropShadow = false;
            var itemStyle = (System.Windows.Style)System.Windows.Markup.XamlReader.Parse("""
                <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="MenuItem">
                  <Setter Property="OverridesDefaultStyle" Value="True"/>
                  <Setter Property="Background" Value="Transparent"/>
                  <Setter Property="Foreground" Value="#ECEFF2"/>
                  <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
                  <Setter Property="Template"><Setter.Value>
                    <ControlTemplate TargetType="MenuItem">
                      <Border x:Name="Row" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                              Background="{TemplateBinding Background}" CornerRadius="6" Padding="12,9">
                        <ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/>
                      </Border>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Row" Property="Background" Value="#303B43"/></Trigger>
                        <Trigger Property="IsEnabled" Value="False"><Setter Property="Foreground" Value="#7D8790"/></Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Setter.Value></Setter>
                </Style>
                """);
            var separatorStyle = (System.Windows.Style)System.Windows.Markup.XamlReader.Parse("""
                <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Separator">
                  <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Separator">
                    <Border Height="1" Margin="10,5" Background="#364049"/>
                  </ControlTemplate></Setter.Value></Setter>
                </Style>
                """);
            void Add(string label, Action action, bool enabled = true)
            {
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = label, IsEnabled = enabled, Style = itemStyle,
                    Padding = new System.Windows.Thickness(12, 8, 12, 8)
                };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
            Add("_Configurações…", () => OpenSettingsRequested?.Invoke());
            Add("Colar _último", () => RepasteLastRequested?.Invoke(), CanRepaste?.Invoke() ?? false);
            Add("Abrir pasta de _logs", () => OpenLogsRequested?.Invoke());
            menu.Items.Add(new System.Windows.Controls.Separator { Style = separatorStyle });
            Add("_Sair", () => ExitRequested?.Invoke());
            SetForegroundWindow(hwnd);
            menu.Closed += (_, _) => System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);
            menu.IsOpen = true;
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true, Name = "Somning.Tray.Menu" };
        popupThread.SetApartmentState(ApartmentState.STA);
        popupThread.Start();
    }
    private nint LoadDefaultIcon()
    {
        var path = Environment.ProcessPath;
        if (path is not null && ExtractIconExW(path, 0, out var large, out var small, 1) > 0)
        {
            if (large != 0) DestroyIcon(large);
            if (small != 0)
            {
                ownsDefaultIcon = true;
                return small;
            }
        }
        return LoadIconW(0, 32512);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string message);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClassW(string name, nint instance);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(nint hMenu, uint uFlags, nint uIDNewItem, string lpNewItem);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint hMenu);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern nint LoadIconW(nint hInstance, int lpIconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconExW(string file, int index, out nint large, out nint small, uint count);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);

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
