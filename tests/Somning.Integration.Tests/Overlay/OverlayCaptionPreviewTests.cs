using System.Runtime.InteropServices;
using Somning.Core.Overlay;
using Somning.Core.Tests.Overlay;
using Somning.Platform.Windows.Overlay;
using Xunit;

namespace Somning.Integration.Tests.Overlay;

/// <summary>
/// Renders the full overlay (orb + GDI caption) through the production path into PNGs. Only runs when
/// SOMNING_PREVIEW_DIR is set; also asserts the caption produces legible, valid premultiplied pixels.
/// </summary>
public sealed class OverlayCaptionPreviewTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount;
        public uint Compression; public uint SizeImage; public int XPels; public int YPels; public uint ClrUsed; public uint ClrImportant;
    }

    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint hdc, ref BitmapInfoHeader bmi, uint usage, out nint bits, nint section, uint offset);

    private static uint[] RenderWithText(OverlayViewModel model, OrbFrame frame, float dpi, out int width, out int height)
    {
        var layout = OrbLayout.Scaled(dpi);
        width = layout.Width;
        height = layout.Height;
        var header = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var dc = CreateCompatibleDC(0);
        var bitmap = CreateDIBSection(dc, ref header, 0, out var bits, 0, 0);
        var old = SelectObject(dc, bitmap);
        try
        {
            var result = new uint[width * height];
            var state = new OverlayWindowManager.CaptionState();
            unsafe
            {
                var span = new Span<uint>((void*)bits, width * height);
                OverlayWindowManager.RenderToBuffer(new OrbRenderer(), span, width, height, dpi, model, in frame, dc, ref state);
                span.CopyTo(result);
            }

            return result;
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bitmap);
            DeleteDC(dc);
        }
    }

    [Fact]
    public void Caption_text_is_legible_and_premultiplied_and_previews_can_be_written()
    {
        var model = OverlayViewModel.CreateNotice(OverlayNoticeKind.EngineFailed);
        var frame = OrbFrame.Static(OverlayMode.Notice, 0.2f);
        var pixels = RenderWithText(model, frame, 1f, out var width, out var height);

        var layout = OrbLayout.Scaled(1f);
        var y0 = (int)(layout.CaptionCenterY - layout.CaptionHeight / 2);
        var white = 0;
        for (var y = y0; y < y0 + (int)layout.CaptionHeight; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = pixels[y * width + x];
                var a = p >> 24;
                Assert.True(((p >> 16) & 0xFF) <= a && ((p >> 8) & 0xFF) <= a && (p & 0xFF) <= a, $"invalid premultiplied pixel {p:X8}");
                if (a > 230 && ((p >> 16) & 0xFF) > 200 && ((p >> 8) & 0xFF) > 200 && (p & 0xFF) > 200) white++;
            }
        }

        Assert.True(white > 150, $"expected white caption glyph pixels, found {white}");

        var dir = Environment.GetEnvironmentVariable("SOMNING_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        foreach (var (name, m, f) in new (string, OverlayViewModel, OrbFrame)[]
        {
            ("full-transcrevendo", OverlayViewModel.CreateTranscribing(), OrbFrame.Static(OverlayMode.Transcribing, 0.2f)),
            ("full-erro-motor", model, frame),
            ("full-erro-timeout", OverlayViewModel.CreateNotice(OverlayNoticeKind.TranscribeTimeout), frame),
        })
        {
            foreach (var dpi in new[] { 1f, 1.5f })
            {
                var px = RenderWithText(m, f, dpi, out var w, out var h);
                var rgb = new byte[w * h * 3];
                PngWriter.Composite(rgb, w, 0, 0, px, w, h, (24, 26, 33));
                PngWriter.WriteRgb(Path.Combine(dir, $"{name}-{dpi:0.0}x.png".Replace(',', '.')), rgb, w, h);
            }
        }
    }
}
