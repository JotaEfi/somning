using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

/// <summary>
/// Paints the "Transcrevendo" caption: a frosted-glass pill (blurred, darkened and saturated desktop behind it,
/// hairline light border, soft shadow), semibold text and three softly pulsing dots instead of an ellipsis. It
/// fades in with a short upward slide. Without a backdrop it falls back to a dark translucent plate.
/// </summary>
internal static class CaptionPainter
{
    public const float HeightDip = 30f;
    public const float GapDip = 14f;
    private const float PaddingDip = 17f;
    private const float DotDip = 3.6f;
    private const float DotGapDip = 3.4f;
    private const float TextDotGapDip = 5f;
    private const float ShadowDip = 11f;
    private const float SlideDip = 7f;

    public static void Draw(Span<uint> pixels, int width, int height, float dpi, TextMask text, float alpha, float seconds,
        float centerX, float centerY, BackdropTexture? backdrop, float backdropMix)
    {
        var eased = FastMath.Smooth(alpha);
        var cy = centerY + (1f - eased) * SlideDip * dpi;
        var dot = DotDip * dpi;
        var dotGap = DotGapDip * dpi;
        var dotsWidth = 3f * dot + 2f * dotGap;
        var content = text.Width + TextDotGapDip * dpi + dotsWidth;
        var halfH = HeightDip * dpi / 2f;
        var halfW = content / 2f + PaddingDip * dpi;
        var core = MathF.Max(0f, halfW - halfH);
        var shadowRange = ShadowDip * dpi;
        var shadowShift = 4f * dpi;
        var mix = backdrop is null ? 0f : backdropMix;

        var x0 = Math.Max(0, (int)(centerX - halfW - shadowRange));
        var x1 = Math.Min(width - 1, (int)(centerX + halfW + shadowRange));
        var y0 = Math.Max(0, (int)(cy - halfH - shadowRange));
        var y1 = Math.Min(height - 1, (int)(cy + halfH + shadowRange + shadowShift));
        var borderWidth = 0.9f * dpi;

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var dx = x + 0.5f - centerX;
                var dy = y + 0.5f - cy;
                var ex = dx - Math.Clamp(dx, -core, core);
                var sd = MathF.Sqrt(ex * ex + dy * dy) - halfH;

                // Soft drop shadow below and around the pill.
                var dys = dy - shadowShift;
                var sds = MathF.Sqrt(ex * ex + dys * dys) - halfH;
                var shadowT = Math.Min(1f, 1f - sds / shadowRange);
                var src = default(Px);
                if (shadowT > 0f)
                {
                    var a = shadowT * shadowT * 0.30f * eased;
                    src = new Px(0f, 0f, 0f, a);
                }

                if (sd < 0.5f)
                {
                    var coverage = Math.Clamp(0.5f - sd, 0f, 1f);
                    var panel = Panel(backdrop, mix, x + 0.5f, y + 0.5f, dy / halfH);

                    // Hairline border, brighter on top, with an inner gradient sheen.
                    var bt = MathF.Abs(sd + 0.55f * dpi) / borderWidth;
                    var border = MathF.Max(0f, 1f - bt);
                    var topLight = Math.Clamp(0.5f - 0.5f * dy / halfH, 0f, 1f);
                    var line = border * (0.12f + 0.34f * topLight);
                    var sheen = 0.07f * topLight * topLight;
                    panel = Px.Over(new Px(line, line, line, line), panel);
                    panel = Px.Add(panel, sheen, sheen, sheen, 0f);
                    src = Px.Over(Px.Scale(panel, coverage * eased), src);
                }

                if (src.A <= 0.001f) continue;
                var index = y * width + x;
                pixels[index] = Px.CompositeOver(pixels[index], src);
            }
        }

        var textCenterX = centerX - (TextDotGapDip * dpi + dotsWidth) / 2f;
        DrawText(pixels, width, height, text, eased * 0.97f, textCenterX, cy);
        DrawDots(pixels, width, height, dpi, text, eased, seconds, textCenterX + text.Width / 2f + TextDotGapDip * dpi, cy);
    }

    // Frosted material: the blurred backdrop, darkened (legibility), saturated and lifted a little on dark desktops.
    private static Px Panel(BackdropTexture? tex, float mix, float x, float y, float v)
    {
        const float panelAlpha = 0.80f;
        var fr = 0.09f;
        var fg = 0.10f;
        var fb = 0.14f;
        if (tex is not null && mix > 0f)
        {
            tex.SampleWindow(x, y, out var r, out var g, out var b);
            var lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            var sat = 1.35f;
            var dark = 0.50f - 0.18f * lum;
            var lift = 0.06f + 0.08f * (1f - lum);
            var tr = (lum + (r - lum) * sat) * dark + lift;
            var tg = (lum + (g - lum) * sat) * dark + lift;
            var tb = (lum + (b - lum) * sat) * dark + lift + 0.012f;
            fr += (Math.Max(0f, tr) - fr) * mix;
            fg += (Math.Max(0f, tg) - fg) * mix;
            fb += (Math.Max(0f, tb) - fb) * mix;
        }

        var a = panelAlpha - 0.08f * (1f - mix);
        var lift2 = 0.035f * (0.5f - v);           // slightly lighter toward the top
        return new Px((fr + lift2) * a, (fg + lift2) * a, (fb + lift2) * a, a);
    }

    private static void DrawDots(Span<uint> pixels, int width, int height, float dpi, TextMask text, float alpha, float seconds, float left, float cy)
    {
        var dot = DotDip * dpi;
        var radius = dot / 2f;
        var baseline = cy + (0.28f * text.Height) - radius - 0.5f * dpi;
        for (var i = 0; i < 3; i++)
        {
            var pulse = CaptionText.DotAlpha(i, seconds);
            var r = radius * (0.82f + 0.18f * pulse);
            var cx = left + radius + i * (dot + DotGapDip * dpi);
            var a = alpha * pulse * 0.95f;
            var y0 = Math.Max(0, (int)(baseline - r - 1));
            var y1 = Math.Min(height - 1, (int)(baseline + r + 1));
            var x0 = Math.Max(0, (int)(cx - r - 1));
            var x1 = Math.Min(width - 1, (int)(cx + r + 1));
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - baseline) * (y + 0.5f - baseline)) - r;
                    var c = Math.Clamp(0.5f - d, 0f, 1f) * a;
                    if (c <= 0f) continue;
                    var index = y * width + x;
                    pixels[index] = Px.CompositeOver(pixels[index], new Px(c, c, c, c));
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
