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

    private readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private readonly WindowProcedure procedure;
    private readonly Thread menuThread;
    private readonly TaskCompletionSource<System.Windows.Threading.Dispatcher> menuReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private nint hwnd;
    private nint defaultIcon;
    private bool ownsDefaultIcon;
    private int disposed;
    private int menuOpen;
    private System.Windows.Threading.Dispatcher? menuDispatcher;
    private System.Windows.Controls.ContextMenu? trayMenu;
    private System.Windows.Controls.MenuItem? repasteItem;
    private System.Windows.Interop.HwndSource? menuSource;
    private System.Windows.Interop.HwndSourceHook? menuSourceHook;
    internal bool TryAcquireMenu() => Interlocked.CompareExchange(ref menuOpen, 1, 0) == 0;
    internal void ReleaseMenu() => Volatile.Write(ref menuOpen, 0);
    private readonly string initialTooltip;
    private uint taskbarCreatedMessage;
    private volatile bool isRegistered;
    public bool IsRegistered => isRegistered;

    public event Action? OpenSettingsRequested;
    public event Action? RepasteLastRequested;
    public event Action? OpenLogsRequested;
    public event Action? CheckForUpdatesRequested;
    public event Action? ExitRequested;

    public Func<bool>? CanRepaste { get; set; }

    public TrayIconManager(string initialTooltip = "Somning - Ditado por Voz")
    {
        this.initialTooltip = initialTooltip;
        procedure = WindowProc;
        thread = new Thread(Run) { IsBackground = true, Name = "Somning.Tray" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            hwnd = ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch
        {
            Interlocked.Exchange(ref disposed, 1);
            StopTrayThread();
            throw;
        }

        menuThread = new Thread(RunMenuDispatcher) { IsBackground = true, Name = "Somning.Tray.Menu" };
        menuThread.SetApartmentState(ApartmentState.STA);
        try
        {
            menuThread.Start();
            menuDispatcher = menuReady.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch
        {
            Interlocked.Exchange(ref disposed, 1);
            RequestMenuShutdown();
            StopTrayThread();
            if (Thread.CurrentThread != menuThread && menuThread.IsAlive)
                menuThread.Join(TimeSpan.FromSeconds(2));
            throw;
        }
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
        if (Volatile.Read(ref disposed) != 0)
        {
            ready.TrySetException(new ObjectDisposedException(nameof(TrayIconManager)));
            return;
        }

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
            if (Volatile.Read(ref disposed) != 0)
            {
                ready.TrySetException(new ObjectDisposedException(nameof(TrayIconManager)));
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
        if (message == 0x0010) { PostQuitMessage(0); return 0; }
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
        if (Volatile.Read(ref disposed) != 0 || !TryAcquireMenu()) return;
        var dispatcher = Volatile.Read(ref menuDispatcher);
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            ReleaseMenu();
            return;
        }

        try
        {
            _ = dispatcher.BeginInvoke(() =>
            {
                if (Volatile.Read(ref disposed) != 0 || trayMenu is null)
                {
                    ReleaseMenu();
                    return;
                }

                try
                {
                    if (repasteItem is not null) repasteItem.IsEnabled = CanRepaste?.Invoke() ?? false;
                    trayMenu.IsOpen = true;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Tray menu failed: {0}", ex);
                    ReleaseMenu();
                }
            });
        }
        catch (InvalidOperationException)
        {
            ReleaseMenu();
        }
    }

    private void RunMenuDispatcher()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        Volatile.Write(ref menuDispatcher, dispatcher);
        try
        {
            dispatcher.UnhandledException += (_, e) =>
            {
                Trace.TraceWarning("Tray menu dispatcher callback failed: {0}", e.Exception);
                e.Handled = true;
            };

            if (Volatile.Read(ref disposed) != 0)
            {
                menuReady.TrySetException(new ObjectDisposedException(nameof(TrayIconManager)));
                return;
            }

            trayMenu = CreateContextMenu();
            menuReady.TrySetResult(dispatcher);
            // A constructor timeout can request shutdown while menu creation is in progress.
            // The queued shutdown runs as soon as the dispatcher starts pumping.
            if (Volatile.Read(ref disposed) != 0) RequestMenuShutdown();
            System.Windows.Threading.Dispatcher.Run();
        }
        catch (Exception ex)
        {
            menuReady.TrySetException(ex);
            Trace.TraceWarning("Tray menu dispatcher failed: {0}", ex);
        }
        finally
        {
            trayMenu = null;
            Volatile.Write(ref menuDispatcher, null);
        }
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu()
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
            StaysOpen = false,
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
            item.Click += (_, _) =>
            {
                menu.IsOpen = false;
                try { action(); }
                catch (Exception ex) { Trace.TraceWarning("Tray menu action failed: {0}", ex); }
            };
            menu.Items.Add(item);
        }
        Add("_Configurações…", () => OpenSettingsRequested?.Invoke());
        Add("Colar _último", () => RepasteLastRequested?.Invoke(), enabled: false);
        repasteItem = (System.Windows.Controls.MenuItem)menu.Items[menu.Items.Count - 1];
        Add("Verificar _atualizações", () => CheckForUpdatesRequested?.Invoke());
        Add("Abrir pasta de _logs", () => OpenLogsRequested?.Invoke());
        menu.Items.Add(new System.Windows.Controls.Separator { Style = separatorStyle });
        Add("_Sair", () => ExitRequested?.Invoke());
        menu.Opened += (_, _) =>
        {
            if (System.Windows.PresentationSource.FromVisual(menu) is System.Windows.Interop.HwndSource source)
            {
                menuSource = source;
                menuSourceHook = (nint window, int message, nint wparam, nint lparam, ref bool handled) =>
                {
                    if (message == 0x0006 && ((long)wparam & 0xFFFF) == 0) menu.IsOpen = false;
                    return 0;
                };
                source.AddHook(menuSourceHook);
                SetForegroundWindow(source.Handle);
            }
            menu.Focus();
        };
        menu.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { menu.IsOpen = false; e.Handled = true; }
        };
        menu.Closed += (_, _) =>
        {
            if (menuSource is not null && menuSourceHook is not null) menuSource.RemoveHook(menuSourceHook);
            menuSource = null;
            menuSourceHook = null;
            ReleaseMenu();
        };
        return menu;
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
        RequestMenuShutdown();
        StopTrayThread();
        if (Thread.CurrentThread != menuThread) menuThread.Join(TimeSpan.FromSeconds(2));
    }

    private void RequestMenuShutdown()
    {
        var dispatcher = Volatile.Read(ref menuDispatcher);
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
        {
            try
            {
                dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        if (trayMenu is not null) trayMenu.IsOpen = false;
                    }
                    finally
                    {
                        dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Send);
                    }
                });
            }
            catch (InvalidOperationException)
            {
                // The dispatcher is already shutting down; its thread will exit on its own.
            }
        }
    }

    private void StopTrayThread()
    {
        if (hwnd != 0) PostMessageW(hwnd, 0x0010 /* WM_CLOSE */, 0, 0);
        if (Thread.CurrentThread != thread) thread.Join(TimeSpan.FromSeconds(2));
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
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClassW(string name, nint instance);
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
