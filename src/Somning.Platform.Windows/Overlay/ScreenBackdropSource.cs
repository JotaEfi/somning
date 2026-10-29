using System.Runtime.InteropServices;
using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

/// <summary>
/// Captures the desktop behind the overlay with a plain GDI BitBlt from the screen DC (no CAPTUREBLT, so layered
/// windows such as the overlay itself are not included; the overlay is also excluded through
/// WDA_EXCLUDEFROMCAPTURE) into one reusable DIB, reduces it to a small detail-preserving texture and wipes the DIB at
/// once. Nothing is saved, logged or sent anywhere; only the reduced texture survives the call.
/// </summary>
public sealed class ScreenBackdropSource : IBackdropSource
{
    private const uint SrcCopy = 0x00CC0020;
    private const uint WdaExcludeFromCapture = 0x00000011;

    private readonly BackdropReducer reducer = new();
    private nint memDc, bitmap, oldBitmap, bits;
    private int dibWidth, dibHeight;

    /// <summary>Excludes <paramref name="window"/> from screen capture (Windows 10 2004+); false when unsupported.</summary>
    public static bool ExcludeFromCapture(nint window) => window != 0 && SetWindowDisplayAffinity(window, WdaExcludeFromCapture);

    public bool TryCapture(in BackdropRegion region, BackdropTexture target)
    {
        if (region.IsEmpty || !EnsureDib(region.Width, region.Height)) return false;

        var screenDc = GetDC(0);
        if (screenDc == 0) return false;
        try
        {
            if (!BitBlt(memDc, 0, 0, region.Width, region.Height, screenDc, region.Left, region.Top, SrcCopy)) return false;
        }
        finally
        {
            ReleaseDC(0, screenDc);
        }

        unsafe
        {
            var pixels = new Span<uint>((void*)bits, region.Width * region.Height);
            try
            {
                reducer.Reduce(pixels, region.Width, region.Height, target);
            }
            finally
            {
                pixels.Clear();   // privacy: the full-resolution capture never outlives this call
            }
        }

        return true;
    }

    private bool EnsureDib(int width, int height)
    {
        if (memDc != 0 && dibWidth == width && dibHeight == height) return true;
        ReleaseDib();

        var screenDc = GetDC(0);
        if (screenDc == 0) return false;
        try
        {
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == 0) return false;
            var bmi = new BITMAPINFO();
            bmi.Header.Size = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.Header.Width = width;
            bmi.Header.Height = -height;
            bmi.Header.Planes = 1;
            bmi.Header.BitCount = 32;
            bitmap = CreateDIBSection(screenDc, ref bmi, 0, out bits, 0, 0);
            if (bitmap == 0 || bits == 0)
            {
                ReleaseDib();
                return false;
            }

            oldBitmap = SelectObject(memDc, bitmap);
            dibWidth = width;
            dibHeight = height;
            return true;
        }
        finally
        {
            ReleaseDC(0, screenDc);
        }
    }

    private void ReleaseDib()
    {
        if (memDc != 0 && oldBitmap != 0) SelectObject(memDc, oldBitmap);
        if (bitmap != 0) DeleteObject(bitmap);
        if (memDc != 0) DeleteDC(memDc);
        memDc = bitmap = oldBitmap = bits = 0;
        dibWidth = dibHeight = 0;
    }

    public void Dispose() => ReleaseDib();

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER Header;
        public uint Colors;
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hWnd, nint hDC);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint hWnd, uint affinity);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint hdcDest, int x, int y, int w, int h, nint hdcSrc, int x1, int y1, uint rop);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO bmi, uint usage, out nint bits, nint section, uint offset);
}
