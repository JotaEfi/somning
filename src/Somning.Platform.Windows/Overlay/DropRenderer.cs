using System.Runtime.CompilerServices;
using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

/// <summary>
/// Software renderer for the translucent 3D glass drop. Every pixel is shaded analytically from a signed-distance
/// style shape (circle with radial lobes, morphing into a capsule): sphere normal, fresnel rim, key/secondary
/// specular, lens-refracted liquid made of flowing color blobs, outer glow and contact shadow. Output is
/// premultiplied ARGB, anti-aliased by distance coverage (no supersampling). Instances own scratch buffers, so
/// rendering allocates nothing; use one instance per render thread.
/// </summary>
public sealed class DropRenderer
{
    private const int Blobs = DropPalette.BlobCount;
    private const float GlowRangeDip = 24f;
    private const float DefaultNativeDpiLimit = 1.25f;
    private const float CaptionGapDip = 17f;
    private const float CaptionPlateAlpha = 0.58f;

    // Blob motion (amplitude xy, frequency xy, phase xy) and size (xy) in sphere-space units.
    private static readonly float[] BlobMotion =
    [
        0.55f, 0.14f, 0.90f, 1.10f, 0.0f, 1.0f,
        0.50f, 0.17f, 1.20f, 0.80f, 2.1f, 0.4f,
        0.45f, 0.20f, 0.70f, 1.30f, 4.0f, 2.2f,
        0.30f, 0.10f, 1.50f, 1.00f, 1.2f, 3.0f,
        0.60f, 0.22f, 0.60f, 0.90f, 5.0f, 0.7f
    ];

    private static readonly float[] BlobSize =
    [
        0.50f, 0.15f,
        0.46f, 0.14f,
        0.42f, 0.13f,
        0.26f, 0.08f,
        0.58f, 0.18f
    ];

    private readonly float[] colors = new float[DropPalette.Length];
    private Blob blob0, blob1, blob2, blob3, blob4;
    /// <summary>Highest DPI scale shaded at native resolution; above it the glass is shaded at 100% and upscaled.</summary>
    public float NativeDpiLimit { get; init; } = DefaultNativeDpiLimit;

    private uint[] internalBuffer = [];
    private int[] columnIndex = [];
    private int[] columnWeight = [];

    /// <summary>
    /// Renders one frame into a premultiplied ARGB buffer (cleared first). Above ~125% DPI the glass is shaded at
    /// 100% resolution and bilinearly upscaled (the shading is smooth), which keeps CPU use flat; text is always
    /// drawn at full resolution.
    /// </summary>
    public void Render(Span<uint> pixels, int width, int height, float dpiScale, in DropFrame frame, TextMask? text = null, float noticeHalfWidth = 0f, TextMask? caption = null)
    {
        pixels.Clear();
        if (frame.Mode == OverlayMode.Hidden || frame.Opacity <= 0.002f || frame.Scale <= 0.02f) return;

        if (dpiScale <= NativeDpiLimit)
        {
            RenderGlass(pixels, width, height, dpiScale, in frame, noticeHalfWidth);
        }
        else
        {
            var k = 1f / dpiScale;
            var iw = (int)MathF.Ceiling(width * k);
            var ih = (int)MathF.Ceiling(height * k);
            if (internalBuffer.Length < iw * ih) internalBuffer = new uint[iw * ih];
            var inner = internalBuffer.AsSpan(0, iw * ih);
            inner.Clear();
            RenderGlass(inner, iw, ih, 1f, in frame, noticeHalfWidth * k);
            Upscale(inner, iw, ih, pixels, width, height);
        }

        if (text is { Width: > 0 } && frame.Morph > 0.5f)
        {
            var textAlpha = Smooth((frame.Morph - 0.55f) / 0.4f) * frame.Opacity;
            DrawText(pixels, width, height, text, textAlpha, width / 2f, height / 2f);
        }

        if (caption is { Width: > 0 } && frame.Mode == OverlayMode.Transcribing)
        {
            var captionAlpha = Smooth((frame.TranscribeMix - 0.1f) / 0.8f) * frame.Opacity;
            if (captionAlpha > 0.01f)
            {
                var centerY = height / 2f + (DropLayout.OrbRadius * frame.Radius * frame.Scale + CaptionGapDip) * dpiScale;
                DrawCaptionPlate(pixels, width, height, caption, captionAlpha, dpiScale, centerY);
                DrawText(pixels, width, height, caption, captionAlpha, width / 2f, centerY);
            }
        }
    }

    private void RenderGlass(Span<uint> pixels, int width, int height, float dpiScale, in DropFrame frame, float noticeHalfWidth)
    {
        var scene = new Scene(frame, width, height, dpiScale, noticeHalfWidth);
        DropPalette.Blend(colors, frame.TranscribeMix, frame.NoticeMix);
        PrepareBlobs(frame);

        var reach = scene.HalfWidth * 1.3f + GlowRangeDip * dpiScale;
        var reachY = scene.HalfHeight * 1.3f + GlowRangeDip * dpiScale;
        var x0 = Math.Max(0, (int)(scene.CenterX - reach));
        var x1 = Math.Min(width - 1, (int)(scene.CenterX + reach));
        var y0 = Math.Max(0, (int)(scene.CenterY - reachY));
        var y1 = Math.Min(height - 1, (int)(scene.CenterY + reachY));

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var px = ShadePixel(in scene, x + 0.5f - scene.CenterX, y + 0.5f - scene.CenterY);
                if (px.A <= 0.0005f) continue;
                pixels[y * width + x] = Pack(px, frame.Opacity);
            }
        }
    }

    // Bilinear upscale of premultiplied ARGB using SWAR lerps (two channels per multiply).
    private void Upscale(ReadOnlySpan<uint> src, int sw, int sh, Span<uint> dst, int dw, int dh)
    {
        if (columnIndex.Length < dw)
        {
            columnIndex = new int[dw];
            columnWeight = new int[dw];
        }

        var fx = (float)sw / dw;
        for (var x = 0; x < dw; x++)
        {
            var sx = Math.Clamp((x + 0.5f) * fx - 0.5f, 0f, sw - 1.001f);
            var ix = (int)sx;
            columnIndex[x] = ix;
            columnWeight[x] = (int)((sx - ix) * 256f);
        }

        var fy = (float)sh / dh;
        for (var y = 0; y < dh; y++)
        {
            var sy = Math.Clamp((y + 0.5f) * fy - 0.5f, 0f, sh - 1.001f);
            var iy = (int)sy;
            var wy = (uint)((sy - iy) * 256f);
            var row0 = src.Slice(iy * sw, sw);
            var row1 = src.Slice(Math.Min(iy + 1, sh - 1) * sw, sw);
            var outRow = dst.Slice(y * dw, dw);
            for (var x = 0; x < dw; x++)
            {
                var ix = columnIndex[x];
                var wx = (uint)columnWeight[x];
                var top = Lerp(row0[ix], row0[ix + 1], wx);
                var bottom = Lerp(row1[ix], row1[ix + 1], wx);
                outRow[x] = Lerp(top, bottom, wy);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Lerp(uint a, uint b, uint t)
    {
        var inv = 256u - t;
        var rb = (((a & 0x00FF00FFu) * inv + (b & 0x00FF00FFu) * t) >> 8) & 0x00FF00FFu;
        var ag = (((a >> 8) & 0x00FF00FFu) * inv + ((b >> 8) & 0x00FF00FFu) * t) & 0xFF00FF00u;
        return rb | ag;
    }

    private void PrepareBlobs(in DropFrame frame)
    {
        var t = frame.Time;
        var grow = 1f + 0.55f * frame.Level;
        var spread = 1f + 0.25f * frame.Level;
        blob0 = MakeBlob(0, t, grow, spread);
        blob1 = MakeBlob(1, t, grow, spread);
        blob2 = MakeBlob(2, t, grow, spread);
        blob3 = MakeBlob(3, t, grow, spread);
        blob4 = MakeBlob(4, t, grow, spread);
    }

    private Blob MakeBlob(int i, float t, float grow, float spread)
    {
        var m = i * 6;
        return new Blob(
            BlobMotion[m] * spread * MathF.Sin(t * BlobMotion[m + 2] + BlobMotion[m + 4]),
            BlobMotion[m + 1] * spread * MathF.Cos(t * BlobMotion[m + 3] + BlobMotion[m + 5]) + 0.14f,
            1f / (BlobSize[i * 2] * grow),
            1f / (BlobSize[i * 2 + 1] * grow),
            colors[i * 3], colors[i * 3 + 1], colors[i * 3 + 2]);
    }

    private readonly record struct Blob(float X, float Y, float InvSx, float InvSy, float R, float G, float B);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Accumulate(in Blob b, float tx, float ty, ref float sum, ref float r, ref float g, ref float bl)
    {
        var ex = (tx - b.X) * b.InvSx;
        var ey = (ty - b.Y) * b.InvSy;
        var w = MathF.ReciprocalEstimate(1f + ex * ex + ey * ey);
        w *= w;
        sum += w;
        r += w * b.R;
        g += w * b.G;
        bl += w * b.B;
    }

    private Px ShadePixel(in Scene s, float dx, float dy)
    {
        var core = Math.Clamp(dx, -s.CoreHalfLength, s.CoreHalfLength);
        var qx = (dx - core) * s.InvRx;
        var qy = dy * s.InvRy;
        var d2 = qx * qx + qy * qy;
        if (d2 > s.RejectLen2) return default;
        var len = MathF.Sqrt(d2);
        var inv = len > 1e-4f ? 1f / len : 0f;
        var zx = qx * inv;
        var zy = qy * inv;

        var z2x = zx * zx - zy * zy;
        var z2y = 2f * zx * zy;
        var z3x = z2x * zx - z2y * zy;
        var z3y = z2x * zy + z2y * zx;
        var z4x = z2x * z2x - z2y * z2y;
        var z4y = 2f * z2x * z2y;
        var r = 1f + s.L1x * zx - s.L1y * zy + s.L2x * z2x - s.L2y * z2y
                   + s.L3x * z3x - s.L3y * z3y + s.L4x * z4x - s.L4y * z4y;

        var u = len / r;
        var edgePx = (1f - u) * r * s.EdgeScale;          // signed distance to the rim in pixels, positive inside
        if (edgePx < -s.GlowRange) return default;

        if (edgePx < -0.5f) return Outside(in s, dx, dy, -edgePx);

        var coverage = Math.Min(1f, edgePx + 0.5f);
        var px = ShadeGlass(in s, dx, dy, qx / r, qy / r, u > 1f ? 1f : u);
        // Soft shadow/glow shows through the anti-aliased rim.
        if (coverage < 1f)
        {
            var outside = Outside(in s, dx, dy, Math.Max(0f, -edgePx));
            px = Over(outside, Scale(px, coverage));
        }

        return px;
    }

    private Px ShadeGlass(in Scene s, float dx, float dy, float nx, float ny, float u)
    {
        var u2 = u * u;
        var nz = MathF.Sqrt(MathF.Max(0f, 1f - u2));
        var rim1 = 1f - nz;
        var fresnel = rim1 * rim1 * rim1;

        // Sphere-space coordinates normalized to the drop's bounding box (ribbons stretch along the capsule).
        var sx = dx * s.InvHalfW;
        var sy = dy * s.InvHalfH;

        // Glass body: dark, mostly transparent in the middle, denser toward the rim, lit from the upper left.
        var lit = MathF.Max(0f, -0.45f * nx - 0.60f * ny + 0.66f * nz);
        var bodyA = 0.20f + 0.40f * rim1 * rim1 + 0.58f * s.NoticeMix;
        var body = new Px(
            (0.018f + 0.050f * lit) * bodyA,
            (0.030f + 0.075f * lit) * bodyA,
            (0.070f + 0.140f * lit) * bodyA,
            bodyA);

        // Liquid seen through a spherical lens: content near the rim is magnified toward it.
        var lens = 1f - 0.30f * u2;
        var lx = sx * lens;
        var ly = sy * lens;
        var twist = s.TranscribeMix * 0.9f * (1f - u);
        var rx = s.RotCos * lx - s.RotSin * ly;
        var ry = s.RotSin * lx + s.RotCos * ly;
        var tx = rx - twist * ry;
        var ty = ry + twist * rx + s.Curve * (tx * tx - 0.18f);

        float sum = 0f, cr = 0f, cg = 0f, cb = 0f;
        Accumulate(in blob0, tx, ty, ref sum, ref cr, ref cg, ref cb);
        Accumulate(in blob1, tx, ty, ref sum, ref cr, ref cg, ref cb);
        Accumulate(in blob2, tx, ty, ref sum, ref cr, ref cg, ref cb);
        Accumulate(in blob3, tx, ty, ref sum, ref cr, ref cg, ref cb);
        Accumulate(in blob4, tx, ty, ref sum, ref cr, ref cg, ref cb);

        var invSum = MathF.ReciprocalEstimate(sum + 1e-4f);
        var intensity = sum * s.LiquidGain;
        var depth = 1f - Smooth((u - 0.80f) / 0.2f) * 0.55f;       // liquid sits inside the glass wall
        var strength = intensity / (intensity + 0.5f) * depth;
        var hot = Math.Clamp(intensity - 1.5f, 0f, 1f) * 0.25f;    // overdriven cores bloom toward white
        var liquidR = Math.Min(1f, cr * invSum + hot);
        var liquidG = Math.Min(1f, cg * invSum + hot);
        var liquidB = Math.Min(1f, cb * invSum + hot);
        var la = Math.Min(0.96f, strength * 1.05f);
        var px = Over(new Px(liquidR * la, liquidG * la, liquidB * la, la), body);

        // Inner glow of the liquid lighting the glass around it.
        var bloom = strength * 0.22f * (1f - u2);
        px = Add(px, liquidR * bloom, liquidG * bloom, liquidB * bloom, bloom * 0.5f);

        // Secondary light leaving through the lower right: a warm crescent hugging the inside of the rim.
        var ring = Smooth((u - 0.58f) / 0.30f) * (1f - Smooth((u - 0.90f) / 0.09f));
        var toward = MathF.Max(0f, nx * 0.55f + ny * 0.83f);
        var crescent = ring * toward * toward * 0.62f * (1f - 0.6f * s.NoticeMix);
        px = Add(px, (0.55f * liquidR + 0.45f) * crescent, (0.55f * liquidG + 0.45f) * crescent, (0.55f * liquidB + 0.45f) * crescent, crescent * 0.8f);

        // Fresnel rim with a cool top-left / warm bottom-right tint (thin-film look) and a hard dark outline for contrast.
        var warm = 0.5f + 0.5f * (nx * 0.6f + ny * 0.8f);
        var rimStrength = fresnel * 0.95f;
        px = Add(px,
            (0.70f + 0.30f * warm) * rimStrength,
            (0.88f - 0.10f * warm) * rimStrength,
            (1.00f - 0.35f * warm) * rimStrength,
            rimStrength * 0.9f);
        var outline = Smooth((u - 0.935f) / 0.065f);
        px = Over(new Px(0f, 0f, 0.02f * outline * 0.35f, 0.35f * outline), px);

        // Key highlight: a soft window-like reflection near the top-left, plus a crisp pin and a faint opposite glint.
        var ex1 = sx + 0.36f * s.HighlightSpread;
        var ey1 = sy + 0.50f;
        var along = (ex1 * 0.82f - ey1 * 0.58f) / (0.30f * s.HighlightSpread);
        var across = (ex1 * 0.58f + ey1 * 0.82f) / 0.12f;
        var window = MathF.Max(0f, 1f - (along * along + across * across));
        window = window * window * (3f - 2f * window);
        var gloss = window * 0.82f;
        if (sx < -0.1f && sy < -0.05f)
        {
            var pin = MathF.Max(0f, 1f - ((sx + 0.27f) * (sx + 0.27f) + (sy + 0.36f) * (sy + 0.36f)) / 0.0016f);
            gloss += pin * pin * 0.55f;
        }

        if (sx > 0.2f && sy > 0.3f)
        {
            var glint = MathF.Max(0f, 1f - ((sx - 0.46f) * (sx - 0.46f) + (sy - 0.55f) * (sy - 0.55f)) / 0.006f);
            gloss += glint * glint * 0.30f;
        }

        // Thin curved gloss band just inside the top rim.
        if (u > 0.80f && ny < -0.30f)
        {
            var topBand = Smooth((u - 0.80f) / 0.08f) * (1f - Smooth((u - 0.93f) / 0.05f)) * Math.Clamp((-ny - 0.30f) / 0.5f, 0f, 1f);
            gloss += topBand * 0.30f;
        }

        gloss = Math.Min(1f, gloss) * s.GlossScale;
        return Add(px, gloss, gloss, gloss, gloss);
    }

    private Px Outside(in Scene s, float dx, float dy, float distancePx)
    {
        // Soft colored glow around the drop that swells with the voice.
        var g = 1f - distancePx / s.GlowRange;
        var result = default(Px);
        if (g > 0f)
        {
            g *= g;
            var a = g * s.GlowAlpha;
            result = new Px(s.GlowR * a, s.GlowG * a, s.GlowB * a, a);
        }

        // Faint contact shadow below, so the drop reads as a 3D object over light backgrounds.
        var sdx = dx - Math.Clamp(dx, -s.CoreHalfLength, s.CoreHalfLength);
        var sdy = dy - s.ShadowOffset;
        var sd = MathF.Sqrt(sdx * sdx + sdy * sdy) - s.ShadowRadius;
        var sh = 1f - sd / s.ShadowSoftness;
        if (sh > 0f)
        {
            sh = Math.Min(1f, sh);
            var a = sh * sh * s.ShadowAlpha;
            result = Over(result, new Px(0f, 0f, 0f, a));
        }

        return result;
    }

    // Dark translucent capsule behind the caption so white text stays legible over light backgrounds.
    private static void DrawCaptionPlate(Span<uint> pixels, int width, int height, TextMask text, float alpha, float dpiScale, float centerY)
    {
        var halfH = text.Height / 2f + 3f * dpiScale;
        var halfW = text.Width / 2f + 10f * dpiScale;
        var cx = width / 2f;
        var core = MathF.Max(0f, halfW - halfH);
        var y0 = Math.Max(0, (int)(centerY - halfH - 1));
        var y1 = Math.Min(height - 1, (int)(centerY + halfH + 1));
        var x0 = Math.Max(0, (int)(cx - halfW - 1));
        var x1 = Math.Min(width - 1, (int)(cx + halfW + 1));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var dx = x + 0.5f - cx;
                var dy = y + 0.5f - centerY;
                var ex = dx - Math.Clamp(dx, -core, core);
                var d = MathF.Sqrt(ex * ex + dy * dy) - halfH;
                var a = Math.Clamp(0.5f - d, 0f, 1f) * alpha * CaptionPlateAlpha;
                if (a <= 0f) continue;
                var dst = pixels[y * width + x];
                var inv = 1f - a;
                var na = a + (dst >> 24) / 255f * inv;
                pixels[y * width + x] = PackBytes(((dst >> 16) & 0xFF) / 255f * inv, ((dst >> 8) & 0xFF) / 255f * inv, (dst & 0xFF) / 255f * inv, na);
            }
        }
    }

    private static void DrawText(Span<uint> pixels, int width, int height, TextMask text, float alpha, float centerX, float centerY)
    {
        var left = (int)MathF.Round(centerX - text.Width / 2f);
        var top = (int)MathF.Round(centerY - text.Height / 2f);
        for (var pass = 0; pass < 2; pass++)
        {
            var shadow = pass == 0;
            var ox = shadow ? 1 : 0;
            var oy = shadow ? 1 : 0;
            for (var y = 0; y < text.Height; y++)
            {
                var py = top + y + oy;
                if ((uint)py >= (uint)height) continue;
                for (var x = 0; x < text.Width; x++)
                {
                    var px = left + x + ox;
                    if ((uint)px >= (uint)width) continue;
                    var cov = text.Coverage[y * text.Width + x] / 255f;
                    if (cov <= 0f) continue;
                    var a = cov * alpha * (shadow ? 0.55f : 1f);
                    var dst = pixels[py * width + px];
                    var inv = 1f - a;
                    var v = shadow ? 0f : 1f;
                    var na = a + ((dst >> 24) / 255f) * inv;
                    var nr = v * a + ((dst >> 16) & 0xFF) / 255f * inv;
                    var ng = v * a + ((dst >> 8) & 0xFF) / 255f * inv;
                    var nb = v * a + (dst & 0xFF) / 255f * inv;
                    pixels[py * width + px] = PackBytes(nr, ng, nb, na);
                }
            }
        }
    }

    // ---- premultiplied color helpers ----

    private readonly record struct Px(float R, float G, float B, float A);

    private static Px Over(Px top, Px bottom)
    {
        var k = 1f - top.A;
        return new Px(top.R + bottom.R * k, top.G + bottom.G * k, top.B + bottom.B * k, top.A + bottom.A * k);
    }

    private static Px Add(Px p, float r, float g, float b, float a) =>
        new(p.R + r, p.G + g, p.B + b, Math.Min(1f, Math.Max(p.A, a) + 0.0f));

    private static Px Scale(Px p, float k) => new(p.R * k, p.G * k, p.B * k, p.A * k);

    private static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static uint Pack(Px p, float opacity) => PackBytes(p.R * opacity, p.G * opacity, p.B * opacity, p.A * opacity);

    private static uint PackBytes(float r, float g, float b, float a)
    {
        var ai = (uint)(Math.Clamp(a, 0f, 1f) * 255f + 0.5f);
        var ri = Math.Min((uint)(Math.Clamp(r, 0f, 1f) * 255f + 0.5f), ai);
        var gi = Math.Min((uint)(Math.Clamp(g, 0f, 1f) * 255f + 0.5f), ai);
        var bi = Math.Min((uint)(Math.Clamp(b, 0f, 1f) * 255f + 0.5f), ai);
        return (ai << 24) | (ri << 16) | (gi << 8) | bi;
    }

    /// <summary>Per-frame constants derived from the animation frame and DPI, so the pixel loop does no setup.</summary>
    private readonly struct Scene
    {
        public readonly float CenterX, CenterY, HalfWidth, HalfHeight, CoreHalfLength, InvRx, InvRy, EdgeScale;
        public readonly float InvHalfW, InvHalfH;
        public readonly float L1x, L1y, L2x, L2y, L3x, L3y, L4x, L4y;
        public readonly float LiquidGain, TranscribeMix, NoticeMix, RotCos, RotSin, HighlightSpread, GlossScale;
        public readonly float Curve, RejectLen2;
        public readonly float GlowRange, GlowAlpha, GlowR, GlowG, GlowB;
        public readonly float ShadowOffset, ShadowRadius, ShadowSoftness, ShadowAlpha;

        public Scene(in DropFrame f, int width, int height, float dpi, float noticeHalfWidth)
        {
            CenterX = width / 2f;
            CenterY = height / 2f;

            var orbR = DropLayout.OrbRadius * dpi * f.Radius;
            var noticeHalfH = DropLayout.NoticeHalfHeight * dpi;
            var noticeHalfW = noticeHalfWidth > 0f ? noticeHalfWidth : noticeHalfH;
            var m = f.Morph;
            HalfHeight = (orbR + (noticeHalfH - orbR) * m) * f.Scale;
            HalfWidth = (orbR + (noticeHalfW - orbR) * m) * f.Scale;
            CoreHalfLength = Math.Max(0f, HalfWidth - HalfHeight);

            var stretchX = 1f + f.Stretch;
            var stretchY = 1f - f.Stretch;
            InvRx = 1f / (HalfHeight * stretchX);
            InvRy = 1f / (HalfHeight * stretchY);
            EdgeScale = HalfHeight * Math.Min(stretchX, stretchY);
            InvHalfW = 1f / (HalfWidth * stretchX);
            InvHalfH = 1f / (HalfHeight * stretchY);

            L1x = f.Lobe1X; L1y = f.Lobe1Y; L2x = f.Lobe2X; L2y = f.Lobe2Y;
            L3x = f.Lobe3X; L3y = f.Lobe3Y; L4x = f.Lobe4X; L4y = f.Lobe4Y;

            TranscribeMix = f.TranscribeMix;
            NoticeMix = f.NoticeMix;
            var breath = 0.5f + 0.5f * MathF.Sin(f.Time * 2.4f);
            var listenGain = 0.42f + 0.10f * breath + 0.85f * f.Level;
            var calmGain = 1.0f + 0.08f * breath;
            LiquidGain = (listenGain + (calmGain - listenGain) * f.TranscribeMix) * (1f - 0.45f * f.NoticeMix);
            var angle = f.TranscribeMix * f.Time * 0.9f;
            RotCos = MathF.Cos(angle);
            RotSin = MathF.Sin(angle);
            Curve = 0.55f * (1f - m) * (1f - 0.7f * f.TranscribeMix);
            HighlightSpread = 1f + 0.35f * m;
            GlossScale = 1f;

            GlowRange = GlowRangeFor(dpi);
            var rejectLen = 1.3f + GlowRange / EdgeScale;
            RejectLen2 = rejectLen * rejectLen;
            GlowAlpha = (0.10f + 0.34f * f.Level + 0.08f * f.TranscribeMix) * (1f - 0.4f * f.NoticeMix);
            GlowR = 0.35f + 0.65f * f.NoticeMix + 0.20f * f.Level;
            GlowG = 0.52f - 0.10f * f.NoticeMix + 0.10f * f.TranscribeMix;
            GlowB = 1.00f - 0.80f * f.NoticeMix;

            ShadowOffset = HalfHeight * 0.30f;
            ShadowRadius = HalfHeight * 0.80f;
            ShadowSoftness = HalfHeight * 0.55f;
            ShadowAlpha = 0.20f;
        }

        private static float GlowRangeFor(float dpi) => GlowRangeDip * dpi;
    }
}
