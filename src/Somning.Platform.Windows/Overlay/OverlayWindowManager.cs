using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

public sealed class OverlayWindowManager : IOverlayView
{
    private const string WindowClassName = "Somning_Overlay_Orb_Window";
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

    private const uint PmRemove = 0x0001;
    private const uint WmQuit = 0x0012;
    private const uint QsAllInput = 0x04FF;
    private const uint Infinite = 0xFFFFFFFF;
    private const long FrameIntervalMs = 16; // ~60 fps

    private const int DtSingleLine = 0x00000020;
    private const int DtVCenter = 0x00000004;
    private const int DtEndEllipsis = 0x00008000;

    private readonly Thread staThread;
    private readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<Action> actionQueue = new();
    private nint hwnd;
    private uint threadId;
    private bool disposed;
    private OverlayViewModel currentModel = OverlayViewModel.CreateHidden();
    private readonly WindowProcedure wndProcDelegate;

    // Render-thread-only animation state (touched exclusively from the overlay STA thread).
    private readonly OrbAnimator animator = new();
    private readonly OrbRenderer renderer = new();
    private OverlayViewModel displayModel = OverlayViewModel.CreateHidden();
    private float pendingLevel;     // written by the audio thread (Volatile), read by the render thread
    private CaptionState captionState;
    private long lastTick;
    private bool windowShown;
    private bool placementDirty;
    private int layoutX, layoutY, layoutWidth, layoutHeight;
    private float dpiScale = 1f;
    private nint surfaceDc, surfaceBitmap, surfaceOldBitmap, surfaceBits;
    private int surfaceWidth, surfaceHeight;

    [ThreadStatic] private static nint cachedFont;
    [ThreadStatic] private static int cachedFontHeight;

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
        Volatile.Write(ref pendingLevel, AudioLevelTracker.LevelFromBars(waveformBars));
        PostModelUpdate(OverlayViewModel.CreateListening(waveformBars));
    }

    public void UpdateWaveform(float[] waveformBars)
    {
        if (disposed || currentModel.Mode != OverlayMode.Listening) return;
        currentModel = OverlayViewModel.CreateListening(waveformBars);
        // Picked up by the next animation frame; no queue traffic per audio buffer.
        Volatile.Write(ref pendingLevel, AudioLevelTracker.LevelFromBars(waveformBars));
    }

    public void ShowTranscribing() => PostModelUpdate(OverlayViewModel.CreateTranscribing());

    public void ShowNotice(OverlayNoticeKind kind) => PostModelUpdate(OverlayViewModel.CreateNotice(kind));

    public void Hide() => PostModelUpdate(OverlayViewModel.CreateHidden());

    private void PostModelUpdate(OverlayViewModel model)
    {
        if (disposed) return;
        currentModel = model;
        if (model.Mode != OverlayMode.Listening) Volatile.Write(ref pendingLevel, 0f);
        actionQueue.Enqueue(() => ApplyModel(model));
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

        RunAnimationLoop();

        ReleaseResources();
        if (hwnd != 0)
        {
            DestroyWindow(hwnd);
            hwnd = 0;
        }
    }

    /// <summary>
    /// Message loop that doubles as the ~60 fps frame clock while the pill is visible or fading, and blocks
    /// without waking while hidden. Never activates or focuses the window.
    /// </summary>
    private void RunAnimationLoop()
    {
        var timerRaised = false;
        var quit = false;

        while (!quit)
        {
            while (PeekMessage(out var msg, 0, 0, 0, PmRemove))
            {
                if (msg.Msg == WmAppQuit || msg.Msg == WmQuit)
                {
                    quit = true;
                    break;
                }

                TranslateMessage(in msg);
                DispatchMessage(in msg);
            }

            if (quit) break;

            DrainActions();

            var frameStart = Stopwatch.GetTimestamp();
            if (animator.IsActive)
            {
                if (!timerRaised)
                {
                    timeBeginPeriod(1);
                    timerRaised = true;
                }

                StepFrame(frameStart);
            }

            if (!animator.IsActive)
            {
                if (timerRaised)
                {
                    timeEndPeriod(1);
                    timerRaised = false;
                }

                if (windowShown) HideWindow();
            }

            MsgWaitForMultipleObjects(0, 0, false, animator.IsActive ? FrameWaitMs(frameStart) : Infinite, QsAllInput);
        }

        if (timerRaised) timeEndPeriod(1);
    }

    private static uint FrameWaitMs(long frameStart)
    {
        var elapsedMs = (Stopwatch.GetTimestamp() - frameStart) * 1000 / Stopwatch.Frequency;
        return (uint)Math.Clamp(FrameIntervalMs - elapsedMs, 1, FrameIntervalMs);
    }

    private void DrainActions()
    {
        while (actionQueue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Trace.WriteLine($"Overlay action failed: {ex}"); }
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

    // ---- Render-thread state changes (called from the pump thread only) ----

    private void ApplyModel(OverlayViewModel model)
    {
        if (hwnd == 0) return;

        if (model.Mode == OverlayMode.Hidden)
        {
            animator.Hide();
            return;
        }

        if (!animator.IsActive) lastTick = Stopwatch.GetTimestamp();

        ComputeLayout();
        displayModel = model;
        animator.Show(model.Mode);
        if (model.Mode == OverlayMode.Listening) animator.SetLevel(AudioLevelTracker.LevelFromBars(model.Bars));
    }

    private void ComputeLayout()
    {
        var fgHwnd = GetForegroundWindow();
        var monitor = MonitorFromWindow(fgHwnd != 0 ? fgHwnd : GetDesktopWindow(), MonitorDefaultToNearest);

        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref mi);

        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        dpiScale = dpi / 96.0f;

        var layout = OrbLayout.Scaled(dpiScale);
        layoutWidth = layout.Width;
        layoutHeight = layout.Height;

        // Bottom center of the active monitor work area.
        layoutX = mi.rcWork.left + (mi.rcWork.right - mi.rcWork.left - layoutWidth) / 2;
        layoutY = mi.rcWork.bottom - layoutHeight - (int)Math.Round(8 * dpiScale);
        placementDirty = true;
    }

    private void StepFrame(long now)
    {
        var dt = (float)((now - lastTick) / (double)Stopwatch.Frequency);
        lastTick = now;

        if (animator.Frame.Mode == OverlayMode.Listening) animator.SetLevel(Volatile.Read(ref pendingLevel));

        if (!animator.Tick(dt)) return;
        DrawFrame();
    }

    private void HideWindow()
    {
        ShowWindow(hwnd, SwHide);
        windowShown = false;
    }

    private void DrawFrame()
    {
        if (hwnd == 0 || layoutWidth <= 0 || layoutHeight <= 0) return;

        var screenDc = GetDC(0);
        if (screenDc == 0) return;

        try
        {
            if (!EnsureSurface(screenDc, layoutWidth, layoutHeight)) return;

            unsafe
            {
                var pixels = new Span<uint>((void*)surfaceBits, layoutWidth * layoutHeight);
                RenderToBuffer(renderer, pixels, layoutWidth, layoutHeight, dpiScale, displayModel, animator.Frame, surfaceDc, ref captionState);
            }

            var ptSrc = new POINT { X = 0, Y = 0 };
            var ptDest = new POINT { X = layoutX, Y = layoutY };
            var size = new SIZE { cx = layoutWidth, cy = layoutHeight };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255, // opacity is baked into the premultiplied pixels
                AlphaFormat = AcSrcAlpha
            };

            UpdateLayeredWindow(hwnd, screenDc, ref ptDest, ref size, surfaceDc, ref ptSrc, 0, ref blend, UlwAlpha);

            if (!windowShown || placementDirty)
            {
                ShowWindow(hwnd, SwShowNoActivate);
                SetWindowPos(hwnd, -1 /* HWND_TOPMOST */, layoutX, layoutY, layoutWidth, layoutHeight, SwpNoActivate | SwpShowWindow);
                windowShown = true;
                placementDirty = false;
            }
        }
        finally
        {
            ReleaseDC(0, screenDc);
        }
    }

    // Cached back buffer: recreated only when the pill size changes (mode/DPI), never per frame.
    private bool EnsureSurface(nint screenDc, int width, int height)
    {
        if (surfaceDc != 0 && surfaceWidth == width && surfaceHeight == height) return true;

        ReleaseSurface();

        var memDc = CreateCompatibleDC(screenDc);
        if (memDc == 0) return false;

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bmi.bmiHeader.biWidth = width;
        bmi.bmiHeader.biHeight = -height; // Top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        var hBitmap = CreateDIBSection(screenDc, ref bmi, 0, out var pBits, 0, 0);
        if (hBitmap == 0 || pBits == 0)
        {
            DeleteDC(memDc);
            return false;
        }

        surfaceOldBitmap = SelectObject(memDc, hBitmap);
        surfaceDc = memDc;
        surfaceBitmap = hBitmap;
        surfaceBits = pBits;
        surfaceWidth = width;
        surfaceHeight = height;
        return true;
    }

    private void ReleaseSurface()
    {
        if (surfaceDc == 0) return;
        SelectObject(surfaceDc, surfaceOldBitmap);
        DeleteObject(surfaceBitmap);
        DeleteDC(surfaceDc);
        surfaceDc = 0;
        surfaceBitmap = 0;
        surfaceBits = 0;
        surfaceOldBitmap = 0;
        surfaceWidth = 0;
        surfaceHeight = 0;
    }

    private void ReleaseResources()
    {
        ReleaseSurface();
        if (cachedFont != 0)
        {
            DeleteObject(cachedFont);
            cachedFont = 0;
            cachedFontHeight = 0;
        }
    }

    // ---- Software rendering (pure pixel work, unit-testable without a window) ----

    /// <summary>Cached caption metrics so the plate width is measured once per text, not once per frame.</summary>
    public struct CaptionState
    {
        internal string? Text;
        internal int FontHeight;
        internal int PlateWidth;
    }

    /// <summary>Renders a static orb frame (no caption text) into a premultiplied BGRA buffer.</summary>
    public static void RenderToBuffer(Span<uint> pixels, int width, int height, float scale, OverlayViewModel model, nint memDc = 0)
    {
        var state = new CaptionState();
        RenderToBuffer(new OrbRenderer(), pixels, width, height, scale, model, OrbFrame.Static(model.Mode, 0.3f), memDc, ref state);
    }

    public static void RenderToBuffer(
        OrbRenderer orb, Span<uint> pixels, int width, int height, float scale,
        OverlayViewModel model, in OrbFrame frame, nint memDc, ref CaptionState caption)
    {
        var text = ShouldShowCaption(frame.Mode) ? model.Text ?? string.Empty : string.Empty;
        var plateWidth = 0;
        if (memDc != 0 && text.Length > 0)
        {
            var fontHeight = (int)Math.Round(13 * scale);
            if (!string.Equals(caption.Text, text, StringComparison.Ordinal) || caption.FontHeight != fontHeight)
            {
                caption = new CaptionState { Text = text, FontHeight = fontHeight, PlateWidth = MeasurePlate(memDc, text, fontHeight, width, scale) };
            }

            plateWidth = caption.PlateWidth;
        }

        orb.Render(pixels, width, height, scale, in frame, plateWidth);

        if (plateWidth > 0)
        {
            RenderText(memDc, pixels, width, height, scale, text, plateWidth, caption.FontHeight);
        }
    }

    // Listening needs no words (the orb is the cue); transcribing and every notice keep a small legible caption.
    private static bool ShouldShowCaption(OverlayMode mode) => mode is OverlayMode.Transcribing or OverlayMode.Notice;

    private static nint EnsureFont(int fontHeight)
    {
        if (cachedFont == 0 || cachedFontHeight != fontHeight)
        {
            if (cachedFont != 0) DeleteObject(cachedFont);
            cachedFont = CreateFontW(-fontHeight, 0, 0, 0, 600 /* FW_SEMIBOLD */, 0, 0, 0, 1 /* DEFAULT_CHARSET */, 0, 0, 0, 0, "Segoe UI");
            cachedFontHeight = fontHeight;
        }

        return cachedFont;
    }

    private static int MeasurePlate(nint memDc, string text, int fontHeight, int width, float scale)
    {
        const int DtCalcRect = 0x00000400;
        var oldFont = SelectObject(memDc, EnsureFont(fontHeight));
        var rect = new RECT { left = 0, top = 0, right = width, bottom = fontHeight * 2 };
        DrawTextW(memDc, text, text.Length, ref rect, DtSingleLine | DtCalcRect);
        SelectObject(memDc, oldFont);
        var padded = rect.right - rect.left + (int)Math.Round(28 * scale);
        return Math.Clamp(padded, (int)Math.Round(60 * scale), width - (int)Math.Round(8 * scale));
    }

    private static void RenderText(nint memDc, Span<uint> pixels, int width, int height, float scale, string text, int plateWidth, int fontHeight)
    {
        const int DtCenter = 0x00000001;
        var layout = OrbLayout.Scaled(scale);
        var oldFont = SelectObject(memDc, EnsureFont(fontHeight));
        SetBkMode(memDc, 1 /* TRANSPARENT */);
        SetTextColor(memDc, 0x00FFFFFF); // white; coverage is recovered from the color channels below

        var padding = (int)Math.Round(12 * scale);
        var left = (width - plateWidth) / 2 + padding;
        var top = (int)Math.Round(layout.CaptionCenterY - layout.CaptionHeight / 2f);
        var rect = new RECT { left = left, top = top, right = left + plateWidth - 2 * padding, bottom = top + (int)Math.Round(layout.CaptionHeight) };
        DrawTextW(memDc, text, text.Length, ref rect, DtSingleLine | DtVCenter | DtCenter | DtEndEllipsis);
        SelectObject(memDc, oldFont);

        // GDI leaves the alpha channel alone, so any channel brighter than alpha is text coverage: lift alpha to it
        // (premultiplied pixels must satisfy rgb <= alpha).
        var y0 = Math.Max(0, rect.top);
        var y1 = Math.Min(height, rect.bottom);
        var x0 = Math.Max(0, rect.left);
        var x1 = Math.Min(width, rect.right);
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var idx = y * width + x;
                var value = pixels[idx];
                var alpha = value >> 24;
                var peak = Math.Max((value >> 16) & 0xFF, Math.Max((value >> 8) & 0xFF, value & 0xFF));
                if (peak > alpha) pixels[idx] = (peak << 24) | (value & 0x00FFFFFF);
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
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] private static extern bool PeekMessage(out Message message, nint window, uint filterMin, uint filterMax, uint removeMsg);
    [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjects(uint count, nint handles, bool waitAll, uint milliseconds, uint wakeMask);
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
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
