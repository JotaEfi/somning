using System.Runtime.CompilerServices;
using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

/// <summary>
/// Software renderer for the translucent 3D glass drop. Every pixel is shaded analytically from a signed-distance
/// style shape (a capsule that the voice elongates, with a teardrop lean and faint radial lobes): sphere normal,
/// neutral fresnel rim, double rim light, inner occlusion, key/secondary specular, voice-driven graphite waves,
/// cyan liquid while transcribing, folded lens refraction of the desktop and a soft contact shadow. Output is premultiplied ARGB,
/// anti-aliased by distance coverage. Instances own scratch buffers, so rendering allocates nothing; use one
/// instance per render thread.
/// </summary>
public sealed partial class DropRenderer
{
    private const float GlowRangeDip = 24f;
    private const float DefaultNativeDpiLimit = 1.25f;

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
    private float[] waveUpper = [], waveCenter = [], waveLower = [];
    private Blob blob0, blob1, blob2, blob3, blob4;

    /// <summary>Highest DPI scale shaded at native resolution; above it the glass is shaded at 100% and upscaled.</summary>
    public float NativeDpiLimit { get; init; } = DefaultNativeDpiLimit;

    private uint[] internalBuffer = [];
    private int[] columnIndex = [];
    private int[] columnWeight = [];

    /// <summary>
    /// Renders one frame into a premultiplied ARGB buffer (cleared first). Above ~125% DPI the glass is shaded at
    /// 100% resolution and bilinearly upscaled (the shading is smooth), which keeps CPU use flat; text is always
    /// drawn at full resolution. <paramref name="backdrop"/> (window-pixel mapped) enables refraction/reflection of
    /// the desktop behind the drop with weight <paramref name="backdropMix"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="restCenterY"/> and <paramref name="edgeY"/> (window pixels, negative = default) place the resting
    /// drop center and the bottom screen edge for the viscous entrance; by default the drop rests at the buffer center
    /// and the edge lies <see cref="DropLayout.DefaultEdgeDistanceDip"/> below it.
    /// </remarks>
    public void Render(Span<uint> pixels, int width, int height, float dpiScale, in DropFrame frame, TextMask? text = null,
        float noticeHalfWidth = 0f, TextMask? caption = null, BackdropTexture? backdrop = null, float backdropMix = 0f,
        float restCenterY = -1f, float edgeY = -1f)
    {
        var rest = restCenterY >= 0f ? restCenterY : height / 2f;
        var edge = edgeY >= 0f ? edgeY : rest + DropLayout.DefaultEdgeDistanceDip * dpiScale;
        pixels.Clear();
        if (frame.Mode == OverlayMode.Hidden || frame.Opacity <= 0.002f || frame.Scale <= 0.02f) return;

        var mix = backdrop is null ? 0f : Math.Clamp(backdropMix, 0f, 1f);
        if (dpiScale <= NativeDpiLimit)
        {
            RenderGlass(pixels, width, height, dpiScale, in frame, noticeHalfWidth, backdrop, mix, 1f, rest, edge);
        }
        else
        {
            var k = 1f / dpiScale;
            var iw = (int)MathF.Ceiling(width * k);
            var ih = (int)MathF.Ceiling(height * k);
            if (internalBuffer.Length < iw * ih) internalBuffer = new uint[iw * ih];
            var inner = internalBuffer.AsSpan(0, iw * ih);
            inner.Clear();
            RenderGlass(inner, iw, ih, 1f, in frame, noticeHalfWidth * k, backdrop, mix, k, rest * k, edge * k);
            Upscale(inner, iw, ih, pixels, width, height);
        }

        if (text is { Width: > 0 } && frame.Morph > 0.5f)
        {
            var textAlpha = FastMath.Smooth((frame.Morph - 0.55f) / 0.4f) * frame.Opacity;
            CaptionPainter.DrawText(pixels, width, height, text, textAlpha, width / 2f, rest);
        }

        if (caption is { Width: > 0 } && frame.Mode == OverlayMode.Transcribing)
        {
            var captionAlpha = FastMath.Smooth((frame.TranscribeMix - 0.1f) / 0.8f) * frame.Opacity;
            if (captionAlpha > 0.01f)
            {
                var centerY = rest + (DropLayout.OrbRadius + CaptionPainter.GapDip + CaptionPainter.HeightDip / 2f) * dpiScale;
                CaptionPainter.Draw(pixels, width, height, dpiScale, caption, captionAlpha, width / 2f, centerY, backdrop, mix);
            }
        }
    }

    private void RenderGlass(Span<uint> pixels, int width, int height, float dpiScale, in DropFrame frame, float noticeHalfWidth,
        BackdropTexture? backdrop, float backdropMix, float pixelScale, float restCenterY, float edgeY)
    {
        var scene = new Scene(frame, width, restCenterY, edgeY, dpiScale, noticeHalfWidth, backdrop, backdropMix, pixelScale);
        if (scene.NeutralMix < 0.999f)
        {
            DropPalette.Blend(colors, frame.TranscribeMix, frame.NoticeMix);
            PrepareBlobs(frame);
        }
        PrepareWaves(in scene, frame, width);

        var reach = scene.HalfWidth * 1.3f + scene.GlowRange;
        var reachY = scene.HalfHeight * 1.6f + scene.GlowRange;
        var x0 = Math.Max(0, (int)(scene.CenterX - reach));
        var x1 = Math.Min(width - 1, (int)(scene.CenterX + reach));
        var y0 = Math.Max(0, (int)(scene.CenterY - reachY));
        var y1 = Math.Min(height - 1, (int)(scene.CenterY + reachY));
        var goo = scene.Goo > 0f;
        if (goo)
        {
            x0 = Math.Max(0, Math.Min(x0, (int)(scene.CenterX - scene.GooReach)));
            x1 = Math.Min(width - 1, Math.Max(x1, (int)(scene.CenterX + scene.GooReach)));
            y1 = Math.Min(height - 1, (int)MathF.Ceiling(scene.CenterY + scene.EdgeDy));
        }

        // Glow and shadow are smooth, so pixels well outside the glass are shaded once per 2x2 block.
        for (var y = y0; y <= y1; y += 2)
        {
            for (var x = x0; x <= x1; x += 2)
            {
                if (goo && InGooBox(in scene, x + 1f - scene.CenterX, y + 1f - scene.CenterY))
                {
                    ShadeGooBlock(pixels, width, height, in scene, x, y, frame.Opacity);
                    continue;
                }

                var fullBlock = x + 1 < width && y + 1 < height;
                if (fullBlock && TryOutside(in scene, x + 1f - scene.CenterX, y + 1f - scene.CenterY, out var outside))
                {
                    if (outside.A <= 0.0005f) continue;
                    var packed = Px.Pack(outside, frame.Opacity);
                    var i0 = y * width + x;
                    pixels[i0] = packed;
                    pixels[i0 + 1] = packed;
                    pixels[i0 + width] = packed;
                    pixels[i0 + width + 1] = packed;
                    continue;
                }

                for (var dy = 0; dy < 2 && y + dy < height; dy++)
                {
                    for (var dx = 0; dx < 2 && x + dx < width; dx++)
                    {
                        var px = ShadePixel(in scene, x + dx + 0.5f - scene.CenterX, y + dy + 0.5f - scene.CenterY);
                        if (px.A <= 0.0005f) continue;
                        pixels[(y + dy) * width + x + dx] = Px.Pack(px, frame.Opacity);
                    }
                }
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

    // Evaluate the oscillators once per column, never once per pixel. These are visual envelopes,
    // driven by RMS and phoneme transients, rather than a frequency spectrum of the microphone.
    private void PrepareWaves(in Scene s, in DropFrame frame, int width)
    {
        if (waveCenter.Length < width)
        {
            waveUpper = new float[width];
            waveCenter = new float[width];
            waveLower = new float[width];
        }

        var amplitude = 0.025f + 0.18f * frame.Level + 0.16f * frame.Ripple;
        // Time is continuous; RipplePhase wraps and cannot drive fractional harmonics.
        var phase = frame.Time * 3.8f;
        for (var x = 0; x < width; x++)
        {
            var u = (x + 0.5f - s.CenterX) * s.InvBoxW;
            var envelope = MathF.Max(0f, 1f - u * u);
            var a = amplitude * envelope;
            waveCenter[x] = 0.04f + a * (0.66f * MathF.Sin(u * 7f - phase) + 0.34f * MathF.Sin(u * 13f + phase * 0.72f));
            waveUpper[x] = -0.11f + a * MathF.Sin(u * 8.5f - phase * 0.85f + 1.2f);
            waveLower[x] = 0.19f + a * MathF.Sin(u * 6f + phase * 0.65f + 2.1f);
        }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Env(in Scene s, float x, float y, out float r, out float g, out float b)
    {
        s.Tex!.SampleTexel(s.BackdropX0 + x * s.BackdropSx, s.BackdropY0 + y * s.BackdropSy, out r, out g, out b);
        var gray = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        r += (gray - r) * s.NeutralMix;
        g += (gray - g) * s.NeutralMix;
        b += (gray - b) * s.NeutralMix;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Fresnel(float u)
    {
        var a = 1f - FastMath.Sqrt(MathF.Max(0f, 1f - u * u));
        return a * a * a;
    }

    // True (with the glow/shadow color) when the point lies at least 2.5 px outside the glass edge.
    private bool TryOutside(in Scene s, float dx, float dy, out Px result)
    {
        result = default;
        var along = Math.Clamp(dx * s.InvHalfWidth, -1f, 1f);
        var dyl = dy * MathF.ReciprocalEstimate(1f + s.Lean * along);
        var core = Math.Clamp(dx, -s.CoreHalfLength, s.CoreHalfLength);
        var qx = (dx - core) * s.InvRx;
        var qy = dyl * s.InvRy;
        var d2 = qx * qx + qy * qy;
        if (d2 > s.RejectLen2) return true;
        if (d2 < 0.5f) return false;
        var len = FastMath.Sqrt(d2);
        var inv = 1f / len;
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
        var edgePx = (1f - len / r) * r * s.EdgeScale;
        if (edgePx < -s.GlowRange) return true;
        if (edgePx > -2.5f) return false;
        result = Outside(in s, dx, dy, -edgePx);
        return true;
    }

    private Px ShadePixel(in Scene s, float dx, float dy)
    {
        // Teardrop lean: the fat end is taller than the thin end.
        var along = Math.Clamp(dx * s.InvHalfWidth, -1f, 1f);
        var leanInv = MathF.ReciprocalEstimate(1f + s.Lean * along);
        var dyl = dy * leanInv;

        var core = Math.Clamp(dx, -s.CoreHalfLength, s.CoreHalfLength);
        var qx = (dx - core) * s.InvRx;
        var qy = dyl * s.InvRy;
        var d2 = qx * qx + qy * qy;
        if (d2 > s.RejectLen2) return default;
        var len = FastMath.Sqrt(d2);
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
        var px = ShadeGlass(in s, dx, dy, dyl, qx / r, qy / r, u > 1f ? 1f : u);
        // Soft shadow/glow shows through the anti-aliased rim.
        if (coverage < 1f)
        {
            var outside = Outside(in s, dx, dy, Math.Max(0f, -edgePx));
            px = Px.Over(outside, Px.Scale(px, coverage));
        }

        return px;
    }

    private Px ShadeGlass(in Scene s, float dx, float dy, float dyl, float nx, float ny, float u)
    {
        var u2 = u * u;
        var nz = FastMath.Sqrt(MathF.Max(0f, 1f - u2));
        var rim1 = 1f - nz;
        var mix = s.BackdropMix;

        // Sphere-space coordinates normalized to the drop's bounding box (ribbons stretch along the capsule).
        var sx = dx * s.InvBoxW;
        var sy = dyl * s.InvBoxH;

        // Glass body: dark, mostly transparent in the middle, denser toward the rim, lit from the upper left.
        // Over a captured backdrop the body gets clearer so the refracted desktop shows through.
        var lit = MathF.Max(0f, -0.45f * nx - 0.60f * ny + 0.66f * nz);
        var bodyA = (0.22f + 0.36f * rim1 * rim1) * (1f - 0.30f * mix) + 0.58f * s.NoticeMix;
        var bodyGray = 0.025f + 0.095f * lit;
        var body = new Px(bodyGray * bodyA, bodyGray * bodyA, bodyGray * bodyA, bodyA);

        // Refraction: a lens shows the desktop compressed toward the center and, near the rim, what lies around it.
        float refR = 0f, refG = 0f, refB = 0f;
        var px = default(Px);
        if (mix > 0f)
        {
            // A folded radial mapping magnifies the center and reverses the local image near the rim.
            // Sample actual backdrop detail, with identical channel coordinates (no invented color fringes).
            var reach = s.RefractReach * (0.90f * u2 - 1.90f * u2 * u2);
            Env(in s, s.CenterX + dx * 0.72f + nx * reach, s.CenterY + dy * 0.72f + ny * reach, out refR, out refG, out refB);
            var lensA = mix * (0.78f + 0.16f * u2);
            px = new Px(refR * lensA, refG * lensA, refB * lensA, lensA);
        }

        px = Px.Over(body, px);

        var depth = 1f - FastMath.Smooth((u - 0.80f) / 0.2f) * 0.55f;
        var strength = 0f;
        var intensity = 0f;
        float liquidR = 0.14f, liquidG = 0.14f, liquidB = 0.14f;
        if (s.NeutralMix > 0f)
        {
            var column = Math.Clamp((int)(s.CenterX + dx), 0, waveCenter.Length - 1);
            var spread = 0.105f + 0.10f * s.VoiceEnergy;
            var d0 = (sy - waveCenter[column]) / spread;
            var d1 = (sy - waveUpper[column]) / (spread * 0.72f);
            var d2 = (sy - waveLower[column]) / (spread * 0.60f);
            var band = 0.65f / (1f + d0 * d0) + 0.28f / (1f + d1 * d1) + 0.22f / (1f + d2 * d2);
            var a = Math.Min(0.84f, band) * depth * s.NeutralMix;
            px = Px.Over(new Px(0.025f * a, 0.025f * a, 0.025f * a, a), px);
            var seam = (sy - waveCenter[column] + spread * 0.50f) / 0.022f;
            var silver = 0.19f / (1f + seam * seam) * depth * s.NeutralMix;
            px = Px.Add(px, silver, silver, silver, silver * 0.65f);
        }

        if (s.NeutralMix < 0.999f)
        {
            // Liquid seen through the lens, with living micro-undulations that ripple harder on phonemes.
            var lens = 1f - 0.30f * u2;
            var lx = sx * lens;
            var ly = sy * lens;
            var twist = s.TranscribeMix * 0.9f * (1f - u);
            var rx = s.RotCos * lx - s.RotSin * ly;
            var ry = s.RotSin * lx + s.RotCos * ly;
            var tx = rx - twist * ry;
            var ty = ry + twist * rx + s.Curve * (tx * tx - 0.18f);
            tx += s.Wobble * FastMath.Sin(ty * 7f + s.WobblePhase);
            ty += s.Wobble * FastMath.Sin(tx * 6f - s.WobblePhase * 0.8f);

            float sum = 0f, cr = 0f, cg = 0f, cb = 0f;
            Accumulate(in blob0, tx, ty, ref sum, ref cr, ref cg, ref cb);
            Accumulate(in blob1, tx, ty, ref sum, ref cr, ref cg, ref cb);
            Accumulate(in blob2, tx, ty, ref sum, ref cr, ref cg, ref cb);
            Accumulate(in blob3, tx, ty, ref sum, ref cr, ref cg, ref cb);
            Accumulate(in blob4, tx, ty, ref sum, ref cr, ref cg, ref cb);

            var invSum = MathF.ReciprocalEstimate(sum + 1e-4f);
            intensity = sum * s.LiquidGain;
            strength = intensity / (intensity + 0.5f) * depth;
            var hot = Math.Clamp(intensity - 1.5f, 0f, 1f) * 0.25f;    // overdriven cores bloom toward white
            liquidR = Math.Min(1f, cr * invSum + hot);
            liquidG = Math.Min(1f, cg * invSum + hot);
            liquidB = Math.Min(1f, cb * invSum + hot);
            if (mix > 0f)
            {
                // A little environmental tint in the cyan/amber states only.
                var tint = 0.16f * mix;
                liquidR += (refR * 0.9f - liquidR) * tint;
                liquidG += (refG * 0.9f - liquidG) * tint;
                liquidB += (refB * 0.9f - liquidB) * tint;
            }

            var la = Math.Min(0.96f, strength * 1.05f) * (1f - s.NeutralMix);
            px = Px.Over(new Px(liquidR * la, liquidG * la, liquidB * la, la), px);
        }

        // Inner occlusion: the glass wall thickens toward the upper left and swallows light there.
        var toward = MathF.Max(0f, nx * 0.55f + ny * 0.83f);
        var wall = FastMath.Smooth((u - 0.70f) / 0.22f) * (1f - FastMath.Smooth((u - 0.95f) / 0.045f));
        var occlusion = wall * 0.30f * (1f - toward) * (1f - 0.5f * s.NoticeMix);
        px = Px.Over(new Px(0f, 0f, 0f, occlusion), px);

        // Inner glow of the liquid lighting the glass around it, plus a wider subsurface scatter.
        var bloom = strength * 0.22f * (1f - u2);
        var scatter = intensity / (intensity + 1.6f) * (1f - u2) * (1f - u2) * 0.26f;
        var glow = bloom + scatter;
        px = Px.Add(px, liquidR * glow, liquidG * glow, liquidB * glow, glow * 0.5f);

        // Secondary light leaving through the lower right: a warm crescent hugging the inside of the rim.
        var ring = FastMath.Smooth((u - 0.58f) / 0.30f) * (1f - FastMath.Smooth((u - 0.90f) / 0.09f));
        var crescent = ring * toward * toward * 0.62f * (1f - 0.6f * s.NoticeMix);
        px = Px.Add(px, (0.55f * liquidR + 0.45f) * crescent, (0.55f * liquidG + 0.45f) * crescent, (0.55f * liquidB + 0.45f) * crescent, crescent * 0.8f);

        // Neutral fresnel while listening; the cyan/amber modes retain their subtle channel dispersion.
        // The outer reflection picks up the actual surroundings.
        var warm = 0.5f + 0.5f * (nx * 0.6f + ny * 0.8f);
        var fresR = Fresnel(u * (0.972f + 0.028f * s.NeutralMix));
        var fresG = Fresnel(u * (0.986f + 0.014f * s.NeutralMix));
        var fresB = Fresnel(u);
        var baseR = 0.90f + (1f - s.NeutralMix) * (-0.20f + 0.30f * warm);
        var baseG = 0.90f + (1f - s.NeutralMix) * (-0.02f - 0.10f * warm);
        var baseB = 0.90f + (1f - s.NeutralMix) * (0.10f - 0.35f * warm);
        if (mix > 0f && u > 0.78f)
        {
            // Two samples straddling the rim give the dispersion: red/blue come from slightly different distances.
            var ui = 1f / MathF.Max(u, 0.05f);
            var ox = nx * ui;
            var oy = ny * ui;
            var d0 = s.EnvReach;
            Env(in s, s.CenterX + dx * 1.1f + ox * d0 * 0.94f, s.CenterY + dy * 1.1f + oy * d0 * 0.94f, out var aR, out var aG, out _);
            Env(in s, s.CenterX + dx * 1.1f + ox * d0 * (1.08f - 0.14f * s.NeutralMix), s.CenterY + dy * 1.1f + oy * d0 * (1.08f - 0.14f * s.NeutralMix), out _, out var bG, out var bB);
            var kEnv = 0.72f * mix;
            baseR += (aR * 1.25f + 0.10f - baseR) * kEnv;
            baseG += ((aG + bG) * 0.625f + 0.10f - baseG) * kEnv;
            baseB += (bB * 1.25f + 0.10f - baseB) * kEnv;
        }

        px = Px.Add(px, baseR * fresR * 0.95f, baseG * fresG * 0.95f, baseB * fresB * 0.95f, Math.Max(fresR, Math.Max(fresG, fresB)) * 0.9f);

        // Second rim light: a thin warm bounce hugging the lower-right edge, picking up the liquid color.
        var bounceBand = FastMath.Smooth((u - 0.80f) / 0.10f) * (1f - FastMath.Smooth((u - 0.965f) / 0.03f));
        var bounce = bounceBand * toward * toward * toward * 0.55f * (1f - 0.7f * s.NoticeMix);
        px = Px.Add(px, (0.45f * liquidR + 0.55f) * bounce, (0.45f * liquidG + 0.45f + 0.10f * s.NeutralMix) * bounce, (0.45f * liquidB + 0.35f + 0.20f * s.NeutralMix) * bounce, bounce * 0.7f);

        // Hard dark outline for contrast on light backgrounds.
        var outline = FastMath.Smooth((u - 0.935f) / 0.065f);
        px = Px.Over(new Px(0f, 0f, 0f, 0.35f * outline * (1f - 0.35f * mix)), px);

        // Key highlight: a soft window-like reflection near the top-left, plus a crisp pin and a faint opposite glint.
        var ex1 = sx + 0.36f * s.HighlightSpread;
        var ey1 = sy + 0.50f;
        var along1 = (ex1 * 0.82f - ey1 * 0.58f) / (0.30f * s.HighlightSpread);
        var across = (ex1 * 0.58f + ey1 * 0.82f) / 0.12f;
        var window = MathF.Max(0f, 1f - (along1 * along1 + across * across));
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
            var topBand = FastMath.Smooth((u - 0.80f) / 0.08f) * (1f - FastMath.Smooth((u - 0.93f) / 0.05f)) * Math.Clamp((-ny - 0.30f) / 0.5f, 0f, 1f);
            gloss += topBand * 0.30f;
        }

        gloss = Math.Min(1f, gloss) * s.GlossScale;
        return Px.Add(px, gloss, gloss, gloss, gloss);
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

        // Broad soft shadow, tinted by what is behind (darker over light backdrops).
        var sdx = dx - Math.Clamp(dx, -s.CoreHalfLength, s.CoreHalfLength);
        var sdy = dy - s.ShadowOffset;
        var sd = FastMath.Sqrt(sdx * sdx + sdy * sdy) - s.ShadowRadius;
        var sh = 1f - sd / s.ShadowSoftness;
        var shadow = default(Px);
        if (sh > 0f)
        {
            sh = Math.Min(1f, sh);
            var a = sh * sh * s.ShadowAlpha;
            shadow = new Px(s.ShadowR * a, s.ShadowG * a, s.ShadowB * a, a);
        }

        // Contact shadow: a tight, darker ellipse right under the drop so it reads as an object, not a sticker.
        if (dy > s.ContactTop)
        {
            var ex = dx * s.InvContactRx;
            var ey = (dy - s.ContactY) * s.InvContactRy;
            var e2 = ex * ex + ey * ey;
            if (e2 < 1f)
            {
                var q = 1f - e2;
                var a = q * q * s.ContactAlpha;
                shadow = Px.Over(new Px(s.ShadowR * a, s.ShadowG * a, s.ShadowB * a, a), shadow);
            }
        }

        return Px.Over(result, shadow);
    }

    private readonly struct Scene
    {
        public readonly float CenterX, CenterY, HalfWidth, HalfHeight, CoreHalfLength, InvRx, InvRy, EdgeScale;
        public readonly float InvBoxW, InvBoxH, InvHalfWidth, Lean;
        public readonly float L1x, L1y, L2x, L2y, L3x, L3y, L4x, L4y;
        public readonly float LiquidGain, TranscribeMix, NoticeMix, RotCos, RotSin, HighlightSpread, GlossScale;
        public readonly float Curve, RejectLen2, Wobble, WobblePhase, NeutralMix, VoiceEnergy;
        public readonly float GlowRange, GlowAlpha, GlowR, GlowG, GlowB;
        public readonly float ShadowOffset, ShadowRadius, ShadowSoftness, ShadowAlpha, ShadowR, ShadowG, ShadowB;
        public readonly float ContactTop, ContactY, InvContactRx, InvContactRy, ContactAlpha;
        public readonly BackdropTexture? Tex;
        public readonly float BackdropMix, BackdropX0, BackdropY0, BackdropSx, BackdropSy, RefractReach, EnvReach;

        public readonly float Goo, EdgeDy, AttachDy, TopEndDy, TopR0, TopR1, BotStartDy, BotR0, BotR1;
        public readonly float PoolCy, InvPoolRx, InvPoolRy, PoolMin, GooK, GooLowK, GooReach;

        public Scene(in DropFrame f, int width, float restCenterY, float edgeY, float dpi, float noticeHalfWidth,
            BackdropTexture? tex, float backdropMix, float pixelScale)
        {
            CenterX = width / 2f;
            CenterY = edgeY + (restCenterY - edgeY) * f.Rise;

            // Volume-preserving squash/stretch: width x(1+e), height (and depth) x1/sqrt(1+e).
            var (widthFactor, heightFactor) = DropStretch.Scales(f.Elongation);
            var orbR = DropLayout.OrbRadius * dpi * f.Radius;
            var noticeHalfH = DropLayout.NoticeHalfHeight * dpi;
            var noticeHalfW = noticeHalfWidth > 0f ? noticeHalfWidth : noticeHalfH;
            var m = f.Morph;
            var orbHalfH = orbR * heightFactor;
            var orbHalfW = orbR * widthFactor;
            HalfHeight = (orbHalfH + (noticeHalfH - orbHalfH) * m) * f.Scale;
            HalfWidth = (orbHalfW + (noticeHalfW - orbHalfW) * m) * f.Scale;
            // Elongated drops get elliptical end caps (organic) that tighten into a true capsule for the notice.
            CoreHalfLength = Math.Max(0f, HalfWidth - HalfHeight) * (0.5f + 0.5f * m);
            var capRx = Math.Max(1f, HalfWidth - CoreHalfLength);
            InvHalfWidth = 1f / Math.Max(1f, HalfWidth);
            Lean = f.Lean;

            // Entrance squash (positive = taller) on top of the voice squash/stretch, roughly volume preserving.
            var stretchX = (1f + f.Stretch) / (1f + 0.5f * f.Squash);
            var stretchY = (1f - f.Stretch) * (1f + f.Squash);
            InvRx = 1f / (capRx * stretchX);
            InvRy = 1f / (HalfHeight * stretchY);
            EdgeScale = Math.Min(capRx, HalfHeight) * Math.Min(stretchX, stretchY);
            InvBoxW = 1f / (HalfWidth * stretchX);
            InvBoxH = 1f / (HalfHeight * stretchY);

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
            Wobble = (0.018f + 0.05f * f.Ripple) * (1f - 0.8f * f.NoticeMix);
            WobblePhase = f.Time * 1.7f + f.RipplePhase * 0.35f;
            NeutralMix = (1f - f.TranscribeMix) * (1f - f.NoticeMix);
            VoiceEnergy = Math.Min(1f, f.Level + 0.65f * f.Ripple);

            GlowRange = GlowRangeDip * dpi;
            var rejectLen = 1.3f + GlowRange / EdgeScale;
            RejectLen2 = rejectLen * rejectLen;
            GlowAlpha = (0.055f + 0.12f * f.Level + 0.08f * f.TranscribeMix) * (1f - 0.4f * f.NoticeMix);
            GlowR = 0.65f * NeutralMix + (1f - NeutralMix) * (0.35f + 0.65f * f.NoticeMix);
            GlowG = 0.65f * NeutralMix + (1f - NeutralMix) * (0.52f - 0.10f * f.NoticeMix + 0.10f * f.TranscribeMix);
            GlowB = 0.65f * NeutralMix + (1f - NeutralMix) * (1f - 0.80f * f.NoticeMix);

            ShadowOffset = HalfHeight * 0.30f;
            ShadowRadius = HalfHeight * 0.80f;
            ShadowSoftness = HalfHeight * 0.55f;
            ContactTop = HalfHeight * 0.70f;
            ContactY = HalfHeight * 1.14f;
            InvContactRx = 1f / (HalfWidth * 0.86f);
            InvContactRy = 1f / (HalfHeight * 0.30f);

            Goo = f.Goo;
            EdgeDy = edgeY - CenterY;
            AttachDy = TopEndDy = TopR0 = TopR1 = BotStartDy = BotR0 = BotR1 = 0f;
            PoolCy = InvPoolRx = InvPoolRy = PoolMin = GooK = GooLowK = GooReach = 0f;
            if (f.Goo > 0f)
            {
                var dropR = HalfHeight * stretchY;
                AttachDy = dropR * 0.55f;
                var pinch = AttachDy + Math.Max(0f, EdgeDy - AttachDy) * DropEmergence.PinchAt;
                TopEndDy = AttachDy + (pinch - AttachDy) * f.TopTail;
                TopR0 = dropR * 0.52f;
                TopR1 = f.Neck * orbR;
                BotStartDy = EdgeDy - (EdgeDy - pinch) * f.BottomTail;
                BotR0 = orbR * Math.Min(0.5f, 0.75f * f.Pool);
                BotR1 = f.Neck * orbR;
                var poolRx = Math.Max(0.5f, orbR * 1.15f * f.Pool);
                var poolRy = Math.Max(0.5f, orbR * 0.62f * f.Pool);
                PoolCy = EdgeDy + poolRy * 0.25f;
                InvPoolRx = f.Pool > 0.01f ? 1f / poolRx : 0f;
                InvPoolRy = 1f / poolRy;
                PoolMin = Math.Min(poolRx, poolRy);
                GooK = orbR * 0.42f;
                // A vanishing mound must not leave a smooth-min bead behind.
                GooLowK = Math.Max(0.5f, GooK * Math.Min(1f, f.Pool / 0.3f));
                GooReach = Math.Max(Math.Max(poolRx, TopR0), HalfWidth) + GlowRangeDip * dpi + GooK;
            }

            Tex = tex;
            BackdropMix = tex is null ? 0f : backdropMix;
            RefractReach = HalfHeight * 1.05f;
            EnvReach = HalfHeight * 1.9f;
            if (tex is null || backdropMix <= 0f)
            {
                BackdropX0 = BackdropY0 = 0f;
                BackdropSx = BackdropSy = 1f;
                ShadowR = ShadowG = ShadowB = 0f;
                ShadowAlpha = 0.20f;
                ContactAlpha = 0.20f;
            }
            else
            {
                // Texel = (window px - origin) / pixelsPerTexel - 0.5, with window px = internal px / pixelScale.
                BackdropSx = 1f / (pixelScale * tex.PixelsPerTexelX);
                BackdropSy = 1f / (pixelScale * tex.PixelsPerTexelY);
                BackdropX0 = -tex.OriginX / tex.PixelsPerTexelX - 0.5f;
                BackdropY0 = -tex.OriginY / tex.PixelsPerTexelY - 0.5f;
                tex.SampleTexel(BackdropX0 + CenterX * BackdropSx, BackdropY0 + (CenterY + HalfHeight * 1.4f) * BackdropSy, out var br, out var bg, out var bb);
                var lum = 0.2126f * br + 0.7152f * bg + 0.0722f * bb;
                var strength = 0.55f + 0.85f * lum;
                ShadowR = (br + (lum - br) * NeutralMix) * 0.16f;
                ShadowG = (bg + (lum - bg) * NeutralMix) * 0.16f;
                ShadowB = (bb + (lum - bb) * NeutralMix) * 0.16f;
                ShadowAlpha = Math.Min(0.30f, 0.14f * strength) * backdropMix + 0.20f * (1f - backdropMix);
                ContactAlpha = Math.Min(0.55f, 0.30f * strength) * backdropMix + 0.20f * (1f - backdropMix);
            }

            // The ground shadow belongs to the floating drop: it fades in as the drop leaves the screen edge.
            var grounded = FastMath.Smooth(f.Rise) * (1f - f.Goo);
            ShadowAlpha *= grounded;
            ContactAlpha *= grounded;
        }
    }
}
