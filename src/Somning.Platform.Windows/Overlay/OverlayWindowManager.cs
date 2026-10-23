using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

public sealed class OverlayWindowManager : IOverlayView
{
    private const string WindowClassName = "Somning_Overlay_Pill_Window";
    private const uint WmApp = 0x8000;
    private const uint WmAppUpdate = WmApp + 101;
    private const uint WmAppQuit = WmApp + 102;

    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsPopup = 0x80000000;

    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int SwShowNoActivate = 4;
    private const int SwHide = 0;

    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const uint MonitorDefaultToNearest = 2;

    private const int DtSingleLine = 0x00000020;
    private const int DtVCenter = 0x00000004;
    private const int DtLeft = 0x00000000;
    private const int DtEndEllipsis = 0x00008000;

    private readonly Thread staThread;
    private readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<Action> actionQueue = new();
    private nint hwnd;
    private uint threadId;
    private bool disposed;
    private OverlayViewModel currentModel = OverlayViewModel.CreateHidden();
    private readonly WindowProcedure wndProcDelegate;

    public OverlayWindowManager()
    {
        wndProcDelegate = WndProc;
        staThread = new Thread(RunMessagePump)
        {
            IsBackground = true,
            Name = "Somning.Overlay.STA"
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        hwnd = ready.Task.GetAwaiter().GetResult();
    }

    public IntPtr Hwnd => hwnd;
    public bool IsVisible => currentModel.Mode != OverlayMode.Hidden;
    public OverlayViewModel CurrentModel => currentModel;

    public void ShowListening(float[] waveformBars)
    {
        var model = OverlayViewModel.CreateListening(waveformBars);
        PostModelUpdate(model);
    }

    public void UpdateWaveform(float[] waveformBars)
    {
        if (currentModel.Mode != OverlayMode.Listening) return;
        var model = OverlayViewModel.CreateListening(waveformBars);
        PostModelUpdate(model);
    }

    public void ShowTranscribing()
    {
        var model = OverlayViewModel.CreateTranscribing();
        PostModelUpdate(model);
    }

    public void ShowNotice(OverlayNoticeKind kind)
    {
        var model = OverlayViewModel.CreateNotice(kind);
        PostModelUpdate(model);
    }

    public void Hide()
    {
        var model = OverlayViewModel.CreateHidden();
        PostModelUpdate(model);
    }

    private void PostModelUpdate(OverlayViewModel model)
    {
        if (disposed) return;
        currentModel = model;
        actionQueue.Enqueue(() => RenderModel(model));
        if (threadId != 0)
        {
            PostThreadMessageW(threadId, WmAppUpdate, 0, 0);
        }
    }

    private void RunMessagePump()
    {
        threadId = GetCurrentThreadId();
        var instance = GetModuleHandle(null);

        var wc = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Style = 0x0001 | 0x0002, // CS_VREDRAW | CS_HREDRAW
            Procedure = Marshal.GetFunctionPointerForDelegate(wndProcDelegate),
            Instance = instance,
            ClassName = WindowClassName
        };

        _ = RegisterClassEx(in wc);

        var exStyle = WsExTopmost | WsExToolWindow | WsExTransparent | WsExNoActivate | WsExLayered;
        var createdHwnd = CreateWindowEx(
            exStyle,
            WindowClassName,
            WindowClassName,
            WsPopup,
            0, 0, 1, 1,
            0, 0, instance, 0);

        if (createdHwnd == 0)
        {
            ready.TrySetException(new System.ComponentModel.Win32Exception());
            return;
        }

        ready.TrySetResult(createdHwnd);

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.Msg == WmAppUpdate)
            {
                while (actionQueue.TryDequeue(out var action))
                {
                    try { action(); }
                    catch (Exception ex) { Trace.WriteLine($"Overlay action failed: {ex}"); }
                }
            }
            else if (msg.Msg == WmAppQuit)
            {
                break;
            }

            TranslateMessage(in msg);
            DispatchMessage(in msg);
        }

        if (hwnd != 0)
        {
            DestroyWindow(hwnd);
            hwnd = 0;
        }
    }

    private nint WndProc(nint window, uint message, nint wParam, nint lParam)
    {
        const uint WmNcHitTest = 0x0084;
        const uint WmMouseActivate = 0x0021;
        const int HtTransparent = -1;
        const int MaNoActivateAndEat = 3;

        switch (message)
        {
            case WmNcHitTest:
                return HtTransparent;
            case WmMouseActivate:
                return MaNoActivateAndEat;
            case 0x0002: // WM_DESTROY
                return 0;
            default:
                return DefWindowProc(window, message, wParam, lParam);
        }
    }

    private void RenderModel(OverlayViewModel model)
    {
        if (hwnd == 0) return;

        if (model.Mode == OverlayMode.Hidden)
        {
            ShowWindow(hwnd, SwHide);
            return;
        }

        // Active monitor and DPI
        var fgHwnd = GetForegroundWindow();
        var monitor = MonitorFromWindow(fgHwnd != 0 ? fgHwnd : GetDesktopWindow(), MonitorDefaultToNearest);

        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref mi);

        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        float scale = dpi / 96.0f;

        // Size calculation
        int baseWidth = model.Mode == OverlayMode.Notice ? 280 : 220;
        int width = (int)Math.Round(baseWidth * scale);
        int height = (int)Math.Round(44 * scale);

        // Position: bottom center of active monitor work area
        int posX = mi.rcWork.left + (mi.rcWork.right - mi.rcWork.left - width) / 2;
        int posY = mi.rcWork.bottom - height - (int)Math.Round(36 * scale);

        var screenDc = GetDC(0);
        if (screenDc == 0) return;

        try
        {
            var memDc = CreateCompatibleDC(screenDc);
            if (memDc == 0) return;

            try
            {
                var bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
                bmi.bmiHeader.biWidth = width;
                bmi.bmiHeader.biHeight = -height; // Top-down
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0; // BI_RGB

                var hBitmap = CreateDIBSection(screenDc, ref bmi, 0, out var pBits, 0, 0);
                if (hBitmap == 0 || pBits == 0) return;

                try
                {
                    var hOldBmp = SelectObject(memDc, hBitmap);

                    unsafe
                    {
                        var pixels = new Span<uint>((void*)pBits, width * height);
                        RenderToBuffer(pixels, width, height, scale, model, memDc);
                    }

                    // 5. UpdateLayeredWindow
                    var ptSrc = new POINT { X = 0, Y = 0 };
                    var ptDest = new POINT { X = posX, Y = posY };
                    var size = new SIZE { cx = width, cy = height };
                    var blend = new BLENDFUNCTION
                    {
                        BlendOp = AcSrcOver,
                        BlendFlags = 0,
                        SourceConstantAlpha = 255,
                        AlphaFormat = AcSrcAlpha
                    };

                    UpdateLayeredWindow(
                        hwnd,
                        screenDc,
                        ref ptDest,
                        ref size,
                        memDc,
                        ref ptSrc,
                        0,
                        ref blend,
                        UlwAlpha);

                    ShowWindow(hwnd, SwShowNoActivate);
                    SetWindowPos(
                        hwnd,
                        -1 /* HWND_TOPMOST */,
                        posX, posY, width, height,
                        SwpNoActivate | SwpShowWindow);

                    SelectObject(memDc, hOldBmp);
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            finally
            {
                DeleteDC(memDc);
            }
        }
        finally
        {
            ReleaseDC(0, screenDc);
        }
    }

    public static void RenderToBuffer(Span<uint> pixels, int width, int height, float scale, OverlayViewModel model, nint memDc = 0)
    {
        // 1. Render rounded pill background
        RenderPillBackground(pixels, width, height, scale);

        // 2. Render status indicator dot
        RenderStatusDot(pixels, width, height, scale, model.Mode);

        // 3. Render waveform bars
        if (model.Mode == OverlayMode.Listening && model.Bars.Length > 0)
        {
            RenderWaveformBars(pixels, width, height, scale, model.Bars);
        }

        // 4. Render text with GDI (if memDc provided)
        if (memDc != 0)
        {
            RenderText(memDc, pixels, width, height, scale, model);
        }
    }

    private static void RenderPillBackground(Span<uint> pixels, int width, int height, float scale)
    {
        float radius = height / 2.0f;
        float borderThickness = 1.2f * scale;

        byte bgA = 0xE6;
        uint bgPixel = ((uint)bgA << 24)
                     | ((uint)(24 * bgA / 255) << 16)
                     | ((uint)(24 * bgA / 255) << 8)
                     | (uint)(27 * bgA / 255);

        uint borderPixel = 0xFF000000 | (70u << 16) | (63u << 8) | 63u;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = x < radius ? radius - x : (x > width - radius ? x - (width - radius) : 0);
                float dy = Math.Abs(y - radius);
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist <= radius - borderThickness)
                {
                    pixels[y * width + x] = bgPixel;
                }
                else if (dist <= radius)
                {
                    pixels[y * width + x] = borderPixel;
                }
                else
                {
                    pixels[y * width + x] = 0;
                }
            }
        }
    }

    private static void RenderStatusDot(Span<uint> pixels, int width, int height, float scale, OverlayMode mode)
    {
        float cx = 20 * scale;
        float cy = height / 2.0f;
        float radius = 5.0f * scale;

        uint dotColor = mode switch
        {
            OverlayMode.Listening => 0xFF000000 | (239u << 16) | (68u << 8) | 68u,     // Red #EF4444
            OverlayMode.Transcribing => 0xFF000000 | (56u << 16) | (189u << 8) | 248u, // Cyan #38BDF8
            OverlayMode.Notice => 0xFF000000 | (245u << 16) | (158u << 8) | 11u,       // Amber #F59E0B
            _ => 0xFF000000 | (200u << 16) | (200u << 8) | 200u
        };

        for (int y = (int)(cy - radius - 1); y <= (int)(cy + radius + 1); y++)
        {
            if (y < 0 || y >= height) continue;
            for (int x = (int)(cx - radius - 1); x <= (int)(cx + radius + 1); x++)
            {
                if (x < 0 || x >= width) continue;
                float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d <= radius)
                {
                    pixels[y * width + x] = dotColor;
                }
            }
        }
    }

    private static void RenderWaveformBars(Span<uint> pixels, int width, int height, float scale, float[] bars)
    {
        int barCount = bars.Length;
        float barWidth = Math.Max(2f, 3.0f * scale);
        float barGap = 3.0f * scale;
        float totalBarsWidth = barCount * barWidth + (barCount - 1) * barGap;

        float startX = width - (24 * scale) - totalBarsWidth;
        float centerY = height / 2.0f;
        float maxBarHeight = height * 0.55f;

        uint barColor = 0xFF000000 | (56u << 16) | (189u << 8) | 248u; // Cyan

        for (int i = 0; i < barCount; i++)
        {
            float energy = Math.Clamp(bars[i], 0.15f, 1.0f);
            float h = Math.Max(4f * scale, maxBarHeight * energy);
            float x0 = startX + i * (barWidth + barGap);
            float y0 = centerY - (h / 2.0f);

            int ix0 = (int)Math.Round(x0);
            int ix1 = (int)Math.Round(x0 + barWidth);
            int iy0 = (int)Math.Round(y0);
            int iy1 = (int)Math.Round(y0 + h);

            for (int y = iy0; y < iy1; y++)
            {
                if (y < 0 || y >= height) continue;
                for (int x = ix0; x < ix1; x++)
                {
                    if (x < 0 || x >= width) continue;
                    pixels[y * width + x] = barColor;
                }
            }
        }
    }

    private static void RenderText(nint memDc, Span<uint> pixels, int width, int height, float scale, OverlayViewModel model)
    {
        string text = model.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text)) return;

        int textStartX = (int)(32 * scale);
        int textEndX = model.Mode == OverlayMode.Listening ? (int)(width - 55 * scale) : (int)(width - 20 * scale);

        var fontHeight = (int)Math.Round(15 * scale);
        var hFont = CreateFontW(
            -fontHeight,
            0, 0, 0,
            600 /* FW_SEMIBOLD */,
            0, 0, 0,
            1 /* DEFAULT_CHARSET */,
            0, 0, 0, 0,
            "Segoe UI");

        var oldFont = SelectObject(memDc, hFont);
        SetBkMode(memDc, 1 /* TRANSPARENT */);
        SetTextColor(memDc, 0x00FFFFFF); // White text

        var rect = new RECT
        {
            left = textStartX,
            top = 0,
            right = textEndX,
            bottom = height
        };

        DrawTextW(
            memDc,
            text,
            text.Length,
            ref rect,
            DtSingleLine | DtVCenter | DtLeft | DtEndEllipsis);

        SelectObject(memDc, oldFont);
        DeleteObject(hFont);

        // Alpha channel fixup for text rendered by GDI
        for (int y = 0; y < height; y++)
        {
            for (int x = textStartX; x < textEndX && x < width; x++)
            {
                int idx = y * width + x;
                uint val = pixels[idx];
                if ((val & 0x00FFFFFF) != 0 && (val >> 24) == 0)
                {
                    pixels[idx] = 0xFFFFFFFF;
                }
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (threadId != 0)
        {
            PostThreadMessageW(threadId, WmAppQuit, 0, 0);
        }

        staThread.Join(1000);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(in WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out Message message, nint window, uint filterMin, uint filterMax);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(in Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(in Message message);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] private static extern bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize,
        nint hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint hgdiobj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint hObject);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO pbmi, uint iUsage, out nint ppvBits, nint hSection, uint dwOffset);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontW(int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
        uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision,
        uint iQuality, uint iPitchAndFamily, string pszFaceName);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint hdc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint hdc, uint color);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(nint hdc, string lpchText, int cchText, ref RECT lprc, uint format);
}
