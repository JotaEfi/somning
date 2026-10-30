using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Somning.Platform.Windows.Settings;

/// <summary>
/// Real blur behind the settings glass. Uses the (undocumented but stable since Windows 10 1803)
/// SetWindowCompositionAttribute acrylic accent with a dark tint, clipped to a rounded window region.
/// Returns false when unavailable so the caller keeps the semi-opaque fallback fill.
/// </summary>
internal static class GlassBackdrop
{
    private const int WcaAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int AccentDisabled = 0;

    /// <summary>Tint in AABBGGRR: near-black graphite at ~70% so the WPF glass layers stay in charge of colour.</summary>
    private const uint DarkTint = 0xB30B0A09;

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

    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);

    public static bool TryEnable(Window window)
    {
        if (Environment.OSVersion.Version.Build < 17134) return false;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return false;
        try
        {
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
        try { SetAccent(hwnd, AccentDisabled, 0); SetWindowRgn(hwnd, 0, true); }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
    }

    /// <summary>Clips the window (and therefore the blur) to the rounded glass rectangle, in device pixels.</summary>
    public static void ClipToRoundedRect(Window window, Rect glassDip, double radiusDip)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var source = PresentationSource.FromVisual(window);
        if (hwnd == 0 || source?.CompositionTarget is null) return;
        var m = source.CompositionTarget.TransformToDevice;
        var region = CreateRoundRectRgn(
            (int)Math.Round(glassDip.Left * m.M11), (int)Math.Round(glassDip.Top * m.M22),
            (int)Math.Round(glassDip.Right * m.M11) + 1, (int)Math.Round(glassDip.Bottom * m.M22) + 1,
            (int)Math.Round(radiusDip * 2 * m.M11), (int)Math.Round(radiusDip * 2 * m.M22));
        if (region == 0) return;
        // On success the system owns the region; delete only on failure.
        if (SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region);
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
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
