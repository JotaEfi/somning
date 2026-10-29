using System.Runtime.InteropServices;

namespace Somning.Platform.Windows.Overlay;

/// <summary>
/// Anti-aliased coverage mask (0..255) for a single line of text, rasterized once with GDI into a private DIB
/// (works headless, no window needed). The renderer composites it, so GDI never touches the layered surface.
/// </summary>
public sealed class TextMask
{
    private TextMask(byte[] coverage, int width, int height)
    {
        Coverage = coverage;
        Width = width;
        Height = height;
    }

    public byte[] Coverage { get; }
    public int Width { get; }
    public int Height { get; }

    public static TextMask Empty { get; } = new([], 0, 0);

    public static TextMask Create(string text, int fontPixels)
    {
        if (string.IsNullOrEmpty(text) || fontPixels <= 0) return Empty;

        var screenDc = GetDC(0);
        var dc = CreateCompatibleDC(screenDc);
        var font = CreateFontW(-fontPixels, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 4 /* ANTIALIASED_QUALITY */, 0, "Segoe UI");
        var oldFont = SelectObject(dc, font);
        try
        {
            GetTextExtentPoint32W(dc, text, text.Length, out var extent);
            var width = extent.cx + 4;
            var height = extent.cy + 4;

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;

            var bitmap = CreateDIBSection(dc, ref bmi, 0, out var bits, 0, 0);
            if (bitmap == 0 || bits == 0) return Empty;

            var oldBitmap = SelectObject(dc, bitmap);
            try
            {
                SetBkMode(dc, 1 /* TRANSPARENT */);
                SetTextColor(dc, 0x00FFFFFF);
                var rect = new RECT { left = 2, top = 2, right = width, bottom = height };
                DrawTextW(dc, text, text.Length, ref rect, 0x20 /* DT_SINGLELINE */ | 0x800 /* DT_NOCLIP */);

                var coverage = new byte[width * height];
                var raw = new byte[width * height * 4];
                Marshal.Copy(bits, raw, 0, raw.Length);
                for (var i = 0; i < coverage.Length; i++) coverage[i] = raw[i * 4 + 1];
                return new TextMask(coverage, width, height);
            }
            finally
            {
                SelectObject(dc, oldBitmap);
                DeleteObject(bitmap);
            }
        }
        finally
        {
            SelectObject(dc, oldFont);
            DeleteObject(font);
            DeleteDC(dc);
            ReleaseDC(0, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

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

    [DllImport("user32.dll")] private static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hWnd, nint hDC);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(nint hdc, string text, int count, ref RECT rect, uint format);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint hdc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint hdc, uint color);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint hdc, ref BITMAPINFO bmi, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern bool GetTextExtentPoint32W(nint hdc, string text, int count, out SIZE size);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);
}
