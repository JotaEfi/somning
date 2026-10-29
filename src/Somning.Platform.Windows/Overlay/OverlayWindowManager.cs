using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

public sealed class OverlayWindowManager : IOverlayView
{
    private const string WindowClassName = "Somning_Overlay_Drop_Window";
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

    private readonly Thread staThread;
    private readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<Action> actionQueue = new();
    private nint hwnd;
    private uint threadId;
    private bool disposed;
    private OverlayViewModel currentModel = OverlayViewModel.CreateHidden();
    private readonly WindowProcedure wndProcDelegate;

    // Render-thread-only animation state (touched exclusively from the overlay STA thread).
    private readonly OverlayAnimator animator = new();
    private readonly DropRenderer renderer = new();
    private float pendingLevel = -1f;   // written by the audio thread, read by the render thread; -1 = none
    private long lastTick;
    private bool windowShown;
    private int anchorCenterX, anchorBottom;
    private int layoutX, layoutY, layoutWidth, layoutHeight;
    private float dpiScale = 1f;
    private TextMask textMask = TextMask.Empty;
    private TextMask captionMask = TextMask.Empty;
    private float noticeHalfWidth;
    private nint surfaceDc, surfaceBitmap, surfaceOldBitmap, surfaceBits;
    private int surfaceWidth, surfaceHeight;

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
        if (model.Mode != OverlayMode.Listening) Volatile.Write(ref pendingLevel, -1f);
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

        ComputeAnchor();
        if (model.Mode == OverlayMode.Notice)
        {
            textMask = TextMask.Create(model.Text ?? string.Empty, (int)Math.Round(15 * dpiScale));
            noticeHalfWidth = DropLayout.NoticeHalfWidth(textMask.Width, dpiScale);
        }
        else if (model.Mode == OverlayMode.Transcribing)
        {
            captionMask = TextMask.Create(model.Text ?? string.Empty, (int)Math.Round(13 * dpiScale));
        }

        animator.Show(model.Mode);
    }

    private void ComputeAnchor()
    {
        var fgHwnd = GetForegroundWindow();
        var monitor = MonitorFromWindow(fgHwnd != 0 ? fgHwnd : GetDesktopWindow(), MonitorDefaultToNearest);

        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref mi);

        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        dpiScale = dpi / 96.0f;

        // Bottom center of the active monitor work area.
        anchorCenterX = mi.rcWork.left + (mi.rcWork.right - mi.rcWork.left) / 2;
        anchorBottom = mi.rcWork.bottom - (int)Math.Round(14 * dpiScale);
    }

    private void StepFrame(long now)
    {
        var dt = (float)((now - lastTick) / (double)Stopwatch.Frequency);
        lastTick = now;

        var level = Volatile.Read(ref pendingLevel);
        if (level >= 0f && animator.Frame.Mode == OverlayMode.Listening) animator.SetLevel(level);

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
        if (hwnd == 0) return;

        var frame = animator.Frame;
        var (width, height) = DropLayout.WindowSize(dpiScale, frame.Morph > 0.01f, textMask.Width);
        layoutWidth = width;
        layoutHeight = height;
        layoutX = anchorCenterX - width / 2;
        layoutY = anchorBottom - height;

        var screenDc = GetDC(0);
        if (screenDc == 0) return;

        try
        {
            if (!EnsureSurface(screenDc, layoutWidth, layoutHeight)) return;

            unsafe
            {
                var pixels = new Span<uint>((void*)surfaceBits, layoutWidth * layoutHeight);
                renderer.Render(pixels, layoutWidth, layoutHeight, dpiScale, in frame, textMask, noticeHalfWidth, captionMask);
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

            if (!windowShown)
            {
                ShowWindow(hwnd, SwShowNoActivate);
                SetWindowPos(hwnd, -1 /* HWND_TOPMOST */, layoutX, layoutY, layoutWidth, layoutHeight, SwpNoActivate | SwpShowWindow);
                windowShown = true;
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

    private void ReleaseResources() => ReleaseSurface();

    /// <summary>Renders a settled frame for the model into a premultiplied ARGB buffer (headless, no window).</summary>
    public static void RenderToBuffer(Span<uint> pixels, int width, int height, float scale, OverlayViewModel model)
    {
        var frame = DropFrame.Static(model.Mode);
        var mask = model.Mode == OverlayMode.Notice
            ? TextMask.Create(model.Text ?? string.Empty, (int)Math.Round(15 * scale))
            : TextMask.Empty;
        var caption = model.Mode == OverlayMode.Transcribing
            ? TextMask.Create(model.Text ?? string.Empty, (int)Math.Round(13 * scale))
            : TextMask.Empty;
        new DropRenderer().Render(pixels, width, height, scale, in frame, mask, DropLayout.NoticeHalfWidth(mask.Width, scale), caption);
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
}
