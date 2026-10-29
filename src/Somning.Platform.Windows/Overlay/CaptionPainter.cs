using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

/// <summary>Translucent, lightly beveled caption glyphs, with a short upward entrance. No plate or dots.</summary>
internal static class CaptionPainter
{
    public const float HeightDip = 30f;
    public const float GapDip = 10f;
    private const float SlideDip = 6f;

    public static void Draw(Span<uint> pixels, int width, int height, float dpi, TextMask text, float alpha,
        float centerX, float centerY, BackdropTexture? backdrop, float backdropMix)
    {
        var cy = centerY + (1f - FastMath.Smooth(alpha)) * SlideDip * dpi;
        var left = (int)MathF.Round(centerX - text.Width / 2f);
        var top = (int)MathF.Round(cy - text.Height / 2f);
        var bevel = Math.Max(1, (int)MathF.Round(dpi));
        // Every pass follows glyph coverage; empty space around/between letters stays transparent.
        for (var pass = 0; pass < 2; pass++)
        {
            var shadow = pass == 0;
            for (var y = 0; y < text.Height; y++)
            {
                var py = top + y + (shadow ? bevel : 0);
                if ((uint)py >= (uint)height) continue;
                for (var x = 0; x < text.Width; x++)
                {
                    var px = left + x;
                    if ((uint)px >= (uint)width) continue;
                    var coverage = text.Coverage[y * text.Width + x] / 255f;
                    if (coverage <= 0f) continue;
                    var a = coverage * alpha * (shadow ? 0.42f : 0.78f);
                    var value = 0f;
                    if (!shadow)
                    {
                        var above = y >= bevel ? text.Coverage[(y - bevel) * text.Width + x] / 255f : 0f;
                        var below = y + bevel < text.Height ? text.Coverage[(y + bevel) * text.Width + x] / 255f : 0f;
                        var luminance = 0.3f;
                        if (backdrop is not null && backdropMix > 0f)
                        {
                            backdrop.SampleWindow(px, py, out var r, out var g, out var b);
                            luminance += (0.2126f * r + 0.7152f * g + 0.0722f * b - luminance) * backdropMix;
                        }

                        value = Math.Clamp(0.94f - 0.28f * luminance + 0.16f * (below - above), 0.45f, 1f);
                    }

                    var index = py * width + px;
                    pixels[index] = Px.CompositeOver(pixels[index], new Px(value * a, value * a, value * a, a));
                }
            }
        }
    }

    /// <summary>White text with a faint dark shadow, composited over the pixel buffer.</summary>
    public static void DrawText(Span<uint> pixels, int width, int height, TextMask text, float alpha, float centerX, float centerY)
    {
        var left = (int)MathF.Round(centerX - text.Width / 2f);
        var top = (int)MathF.Round(centerY - text.Height / 2f);
        for (var pass = 0; pass < 2; pass++)
        {
            var shadow = pass == 0;
            var offset = shadow ? 1 : 0;
            for (var y = 0; y < text.Height; y++)
            {
                var py = top + y + offset;
                if ((uint)py >= (uint)height) continue;
                for (var x = 0; x < text.Width; x++)
                {
                    var px = left + x + offset;
                    if ((uint)px >= (uint)width) continue;
                    var cov = text.Coverage[y * text.Width + x] / 255f;
                    if (cov <= 0f) continue;
                    var a = cov * alpha * (shadow ? 0.28f : 1f);
                    var v = shadow ? 0f : 1f;
                    var index = py * width + px;
                    pixels[index] = Px.CompositeOver(pixels[index], new Px(v * a, v * a, v * a, a));
                }
            }
        }
    }
}
