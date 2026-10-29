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

    private const uint PmRemove = 0x0001;
    private const uint WmQuit = 0x0012;
    private const uint QsAllInput = 0x04FF;
    private const uint Infinite = 0xFFFFFFFF;
    private const long FrameIntervalMs = 16; // ~60 fps

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

    // Render-thread-only animation state (touched exclusively from the overlay STA thread).
    private readonly OverlayAnimator animator = new();
    private OverlayViewModel displayModel = OverlayViewModel.CreateHidden();
    private float[]? pendingBars;   // written by the audio thread, read by the render thread
    private float[]? appliedBars;
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
        Volatile.Write(ref pendingBars, waveformBars);
        PostModelUpdate(OverlayViewModel.CreateListening(waveformBars));
    }

    public void UpdateWaveform(float[] waveformBars)
    {
        if (disposed || currentModel.Mode != OverlayMode.Listening) return;
        currentModel = OverlayViewModel.CreateListening(waveformBars);
        // Picked up by the next animation frame; no queue traffic per audio buffer.
        Volatile.Write(ref pendingBars, waveformBars);
    }

    public void ShowTranscribing() => PostModelUpdate(OverlayViewModel.CreateTranscribing());

    public void ShowNotice(OverlayNoticeKind kind) => PostModelUpdate(OverlayViewModel.CreateNotice(kind));

    public void Hide() => PostModelUpdate(OverlayViewModel.CreateHidden());

    private void PostModelUpdate(OverlayViewModel model)
    {
        if (disposed) return;
        currentModel = model;
        if (model.Mode != OverlayMode.Listening) Volatile.Write(ref pendingBars, null);
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

        ComputeLayout(model.Mode);
        displayModel = model;
        animator.Show(model.Mode);
        if (model.Mode == OverlayMode.Listening) animator.SetLiveBars(model.Bars);
    }

    private void ComputeLayout(OverlayMode mode)
    {
        var fgHwnd = GetForegroundWindow();
        var monitor = MonitorFromWindow(fgHwnd != 0 ? fgHwnd : GetDesktopWindow(), MonitorDefaultToNearest);

        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref mi);

        uint dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) dpi = 96;
        dpiScale = dpi / 96.0f;

        int baseWidth = mode == OverlayMode.Notice ? 280 : 220;
        layoutWidth = (int)Math.Round(baseWidth * dpiScale);
        layoutHeight = (int)Math.Round(44 * dpiScale);

        // Bottom center of the active monitor work area.
        layoutX = mi.rcWork.left + (mi.rcWork.right - mi.rcWork.left - layoutWidth) / 2;
        layoutY = mi.rcWork.bottom - layoutHeight - (int)Math.Round(36 * dpiScale);
        placementDirty = true;
    }

    private void StepFrame(long now)
    {
        var dt = (float)((now - lastTick) / (double)Stopwatch.Frequency);
        lastTick = now;

        var bars = Volatile.Read(ref pendingBars);
        if (bars is not null && animator.Frame.Mode == OverlayMode.Listening && !ReferenceEquals(bars, appliedBars))
        {
            animator.SetLiveBars(bars);
            appliedBars = bars;
        }

        if (!animator.Tick(dt)) return;
        DrawFrame();
    }

    private void HideWindow()
    {
        ShowWindow(hwnd, SwHide);
        windowShown = false;
        appliedBars = null;
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
                RenderToBuffer(pixels, layoutWidth, layoutHeight, dpiScale, displayModel, animator.Frame, animator.Bars, surfaceDc);
            }

            var ptSrc = new POINT { X = 0, Y = 0 };
            var ptDest = new POINT { X = layoutX, Y = layoutY };
            var size = new SIZE { cx = layoutWidth, cy = layoutHeight };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = (byte)Math.Round(Math.Clamp(animator.Frame.Opacity, 0f, 1f) * 255f),
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

    public static void RenderToBuffer(Span<uint> pixels, int width, int height, float scale, OverlayViewModel model, nint memDc = 0)
        => RenderToBuffer(pixels, width, height, scale, model, OverlayFrame.Static(model.Mode), model.Bars, memDc);

    public static void RenderToBuffer(
        Span<uint> pixels, int width, int height, float scale,
        OverlayViewModel model, in OverlayFrame frame, ReadOnlySpan<float> bars, nint memDc = 0)
    {
        RenderPillBackground(pixels, width, height, scale, frame);
        RenderStatusDot(pixels, width, height, scale, frame);

        if (frame.Mode is OverlayMode.Listening or OverlayMode.Transcribing && bars.Length > 0)
        {
            RenderWaveformBars(pixels, width, height, scale, frame, bars);
        }

        if (memDc != 0)
        {
            RenderText(memDc, pixels, width, height, scale, model);
        }
    }

    private static uint Accent(OverlayMode mode) => mode switch
    {
        OverlayMode.Listening => Rgb(239, 68, 68),     // Red #EF4444
        OverlayMode.Transcribing => Rgb(56, 189, 248), // Cyan #38BDF8
        OverlayMode.Notice => Rgb(245, 158, 11),       // Amber #F59E0B
        _ => Rgb(200, 200, 200)
    };

    private static uint Rgb(uint r, uint g, uint b) => 0xFF000000 | (r << 16) | (g << 8) | b;

    /// <summary>Per-channel (premultiplied-safe) linear blend of two ARGB pixels.</summary>
    private static uint Lerp(uint from, uint to, float t)
    {
        if (t <= 0f) return from;
        if (t >= 1f) return to;
        var inv = 1f - t;
        var a = (uint)((from >> 24) * inv + (to >> 24) * t + 0.5f);
        var r = (uint)(((from >> 16) & 0xFF) * inv + ((to >> 16) & 0xFF) * t + 0.5f);
        var g = (uint)(((from >> 8) & 0xFF) * inv + ((to >> 8) & 0xFF) * t + 0.5f);
        var b = (uint)((from & 0xFF) * inv + (to & 0xFF) * t + 0.5f);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static uint Fade(uint pixel, float coverage) => Lerp(0, pixel, coverage);

    private static void RenderPillBackground(Span<uint> pixels, int width, int height, float scale, in OverlayFrame frame)
    {
        float s = frame.Scale;
        float pillW = width * s;
        float pillH = height * s;
        float offX = (width - pillW) / 2f;
        float offY = (height - pillH) / 2f;
        float radius = pillH / 2.0f;
        float borderThickness = 1.2f * scale;

        byte bgA = 0xE6;
        uint bgPixel = ((uint)bgA << 24)
                     | ((uint)(24 * bgA / 255) << 16)
                     | ((uint)(24 * bgA / 255) << 8)
                     | (uint)(27 * bgA / 255);

        // Border takes a soft tint of the state color that breathes with the pulse and brightens with speech.
        uint baseBorder = Rgb(70, 63, 63);
        float glow = frame.Mode is OverlayMode.Listening or OverlayMode.Transcribing
            ? Math.Min(0.6f, 0.4f * frame.Pulse + 0.4f * frame.Energy)
            : 0f;
        uint borderPixel = Lerp(baseBorder, Accent(frame.Mode), glow);

        for (int y = 0; y < height; y++)
        {
            float py = y + 0.5f - offY;
            for (int x = 0; x < width; x++)
            {
                float px = x + 0.5f - offX;
                if (px < 0f || px > pillW || py < 0f || py > pillH)
                {
                    pixels[y * width + x] = 0;
                    continue;
                }

                float dx = px < radius ? radius - px : (px > pillW - radius ? px - (pillW - radius) : 0f);
                float dy = Math.Abs(py - radius);
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                float coverage = Math.Clamp(radius - dist + 0.5f, 0f, 1f);
                if (coverage <= 0f)
                {
                    pixels[y * width + x] = 0;
                    continue;
                }

                float inner = Math.Clamp(radius - borderThickness - dist + 0.5f, 0f, 1f);
                pixels[y * width + x] = Fade(Lerp(borderPixel, bgPixel, inner), coverage);
            }
        }
    }

    private static void RenderStatusDot(Span<uint> pixels, int width, int height, float scale, in OverlayFrame frame)
    {
        float s = frame.Scale;
        float cx = width / 2f + (20 * scale - width / 2f) * s;
        float cy = height / 2.0f;
        float radius = 5.0f * scale * s;
        uint color = Accent(frame.Mode);

        // Soft halo: pulses while idle, swells with the voice.
        float haloAlpha = frame.Mode is OverlayMode.Listening or OverlayMode.Transcribing
            ? 0.22f * frame.Pulse + 0.30f * frame.Energy
            : 0f;
        float haloRadius = radius + 5f * scale * s * (0.6f + 0.4f * frame.Pulse);
        float reach = haloAlpha > 0.001f ? haloRadius : radius;

        for (int y = (int)(cy - reach - 1); y <= (int)(cy + reach + 1); y++)
        {
            if (y < 0 || y >= height) continue;
            for (int x = (int)(cx - reach - 1); x <= (int)(cx + reach + 1); x++)
            {
                if (x < 0 || x >= width) continue;
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                ref uint px = ref pixels[y * width + x];

                if (haloAlpha > 0.001f && d > radius - 0.5f && d < haloRadius)
                {
                    float falloff = 1f - (d - radius) / (haloRadius - radius);
                    falloff = Math.Clamp(falloff, 0f, 1f);
                    px = Lerp(px, color, haloAlpha * falloff * falloff);
                }

                float dotCoverage = Math.Clamp(radius - d + 0.5f, 0f, 1f);
                if (dotCoverage > 0f) px = Lerp(px, color, dotCoverage);
            }
        }
    }

    private static void RenderWaveformBars(Span<uint> pixels, int width, int height, float scale, in OverlayFrame frame, ReadOnlySpan<float> bars)
    {
        float s = frame.Scale;
        int barCount = bars.Length;
        float barWidth = Math.Max(2f, 3.0f * scale);
        float barGap = 3.0f * scale;
        float totalBarsWidth = barCount * barWidth + (barCount - 1) * barGap;

        float startX = width - (24 * scale) - totalBarsWidth;
        float centerY = height / 2.0f;
        float maxBarHeight = height * 0.55f;

        uint bright = Rgb(56, 189, 248); // Cyan
        uint dim = Rgb(28, 94, 124);     // Same hue, quieter, for the transcribing shimmer
        bool shimmer = frame.Mode == OverlayMode.Transcribing;

        for (int i = 0; i < barCount; i++)
        {
            float energy = Math.Clamp(bars[i], 0.15f, 1.0f);
            float h = Math.Max(4f * scale, maxBarHeight * energy) * s;
            float w = barWidth * s;
            float cx = width / 2f + (startX + i * (barWidth + barGap) + barWidth / 2f - width / 2f) * s;

            uint color = shimmer ? Lerp(dim, bright, Math.Clamp((energy - 0.2f) / 0.65f, 0f, 1f)) : bright;
            DrawCapsule(pixels, width, height, cx, centerY, w / 2f, h / 2f, color);
        }
    }

    // Vertical anti-aliased capsule (rounded bar); halfW is also the cap radius.
    private static void DrawCapsule(Span<uint> pixels, int width, int height, float cx, float cy, float halfW, float halfH, uint color)
    {
        float straight = Math.Max(0f, halfH - halfW);
        int x0 = Math.Max(0, (int)MathF.Floor(cx - halfW - 1));
        int x1 = Math.Min(width - 1, (int)MathF.Ceiling(cx + halfW + 1));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - halfH - 1));
        int y1 = Math.Min(height - 1, (int)MathF.Ceiling(cy + halfH + 1));

        for (int y = y0; y <= y1; y++)
        {
            float qy = Math.Max(Math.Abs(y + 0.5f - cy) - straight, 0f);
            for (int x = x0; x <= x1; x++)
            {
                float qx = Math.Abs(x + 0.5f - cx);
                float d = MathF.Sqrt(qx * qx + qy * qy) - halfW;
                float coverage = Math.Clamp(0.5f - d, 0f, 1f);
                if (coverage <= 0f) continue;
                ref uint px = ref pixels[y * width + x];
                px = Lerp(px, color, coverage);
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
        if (cachedFont == 0 || cachedFontHeight != fontHeight)
        {
            if (cachedFont != 0) DeleteObject(cachedFont);
            cachedFont = CreateFontW(
                -fontHeight,
                0, 0, 0,
                600 /* FW_SEMIBOLD */,
                0, 0, 0,
                1 /* DEFAULT_CHARSET */,
                0, 0, 0, 0,
                "Segoe UI");
            cachedFontHeight = fontHeight;
        }

        var oldFont = SelectObject(memDc, cachedFont);
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
