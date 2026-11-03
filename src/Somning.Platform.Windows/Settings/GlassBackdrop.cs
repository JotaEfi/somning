using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Somning.Platform.Windows.Settings;

/// <summary>
/// DWM blur behind a borderless HWND, limited to the same rounded rectangle as the WPF glass. The blur region
/// (hRgnBlur) clips the blur itself; the Windows 10 acrylic accent ignored the window region and left square corners.
/// </summary>
internal static class GlassBackdrop
{
    private const uint DwmBbEnable = 0x1;
    private const uint DwmBbBlurRegion = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public uint Flags;
        public int Enable;
        public nint RegionBlur;
        public int TransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref BlurBehind blurBehind);
    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);
    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    private static extern bool EqualRgn(nint first, nint second);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(nint hwnd, nint region);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);

    public static bool TryEnable(Window window)
    {
        if (Environment.OSVersion.Version.Build < 17134) return false;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return false;
        try
        {
            // WindowChrome maintains its own window region and can replace ours after
            // WM_WINDOWPOSCHANGED. Do not attach it: extend the transparent client ourselves.
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
                target.BackgroundColor = Colors.Transparent;
            var disableNonClientRendering = 1; // DWMNCRP_DISABLED: no square native frame around our glass.
            _ = DwmSetWindowAttribute(hwnd, 2, ref disableNonClientRendering, sizeof(int));
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);
            // Blurs the whole client until ClipToRoundedRect supplies the glass region.
            return SetBlur(hwnd, enable: true, region: 0);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    public static void Disable(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return;
        try { SetBlur(hwnd, enable: false, region: 0); }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
    }

    /// <summary>Clips the blur and the native window to the WPF glass bounds in device pixels.</summary>
    public static void ClipToRoundedRect(Window window, Rect glassDip, double radiusDip)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        if (hwnd == 0 || source?.CompositionTarget is null) return;
        var m = source.CompositionTarget.TransformToDevice;
        var region = CreateGlassRegion(m, glassDip, radiusDip);
        if (region == 0) return;
        var existing = CreateRectRgn(0, 0, 0, 0);
        nint blurRegion = 0;
        try
        {
            // Setting a region sends another window-position notification. Comparing
            // native regions prevents endless reapplication while still repairing a reset.
            if (existing != 0 && GetWindowRgn(hwnd, existing) != 0 && EqualRgn(existing, region))
                return;
            // SetWindowRgn keeps the region it receives, so the blur gets its own copy.
            blurRegion = CreateGlassRegion(m, glassDip, radiusDip);
            if (blurRegion != 0) SetBlur(hwnd, enable: true, region: blurRegion);
            if (SetWindowRgn(hwnd, region, true) != 0) region = 0; // The system now owns it.
        }
        finally
        {
            if (existing != 0) DeleteObject(existing);
            if (region != 0) DeleteObject(region);
            if (blurRegion != 0) DeleteObject(blurRegion);
        }
    }

    private static nint CreateGlassRegion(Matrix m, Rect glassDip, double radiusDip) => CreateRoundRectRgn(
        (int)Math.Round(glassDip.Left * m.M11), (int)Math.Round(glassDip.Top * m.M22),
        (int)Math.Round(glassDip.Right * m.M11), (int)Math.Round(glassDip.Bottom * m.M22),
        (int)Math.Round(radiusDip * 2 * m.M11), (int)Math.Round(radiusDip * 2 * m.M22));

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableBlurBehind = 3;

    /// <summary>
    /// Gaussian blur behind the window without the fixed whitish haze that DwmEnableBlurBehindWindow adds on
    /// Windows 10. The rounded shape comes from the window region applied in <see cref="ClipToRoundedRect"/>.
    /// </summary>
    private static bool SetBlur(nint hwnd, bool enable, nint region)
    {
        var accent = new AccentPolicy { AccentState = enable ? AccentEnableBlurBehind : AccentDisabled };
        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, buffer, false);
            var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = buffer, SizeOfData = size };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
