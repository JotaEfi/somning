using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Somning.Platform.Windows.Settings;

/// <summary>Native acrylic on a borderless HWND, clipped to the same rounded rectangle as the WPF glass.</summary>
internal static class GlassBackdrop
{
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int AccentDisabled = 0;

    // Original composite opacity: tint 102/255 plus fill 80/96/120 over that tint.
    // New tint is 61/255; XAML fill 38/46/57 produces approximately 60% of the old
    // composite opacity at each stop (rather than multiplying both layers by .6).
    private const uint DarkTint = 0x3D0B0A09;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
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
            return SetAccent(hwnd, AccentEnableAcrylicBlurBehind, DarkTint);
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
        try { SetAccent(hwnd, AccentDisabled, 0); }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
    }

    /// <summary>Clips the actual native surface, including acrylic, to the WPF glass bounds in device pixels.</summary>
    public static void ClipToRoundedRect(Window window, Rect glassDip, double radiusDip)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        if (hwnd == 0 || source?.CompositionTarget is null) return;
        var m = source.CompositionTarget.TransformToDevice;
        var region = CreateRoundRectRgn(
            (int)Math.Round(glassDip.Left * m.M11), (int)Math.Round(glassDip.Top * m.M22),
            (int)Math.Round(glassDip.Right * m.M11), (int)Math.Round(glassDip.Bottom * m.M22),
            (int)Math.Round(radiusDip * 2 * m.M11), (int)Math.Round(radiusDip * 2 * m.M22));
        if (region == 0) return;
        var existing = CreateRectRgn(0, 0, 0, 0);
        try
        {
            // Setting a region sends another window-position notification. Comparing
            // native regions prevents endless reapplication while still repairing a reset.
            if (existing != 0 && GetWindowRgn(hwnd, existing) != 0 && EqualRgn(existing, region))
                return;
            if (SetWindowRgn(hwnd, region, true) != 0) region = 0; // The system now owns it.
        }
        finally
        {
            if (existing != 0) DeleteObject(existing);
            if (region != 0) DeleteObject(region);
        }
    }

    private static bool SetAccent(nint hwnd, int state, uint tint)
    {
        var policy = new AccentPolicy { AccentState = state, AccentFlags = 2, GradientColor = tint };
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = ptr, SizeOfData = size };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
