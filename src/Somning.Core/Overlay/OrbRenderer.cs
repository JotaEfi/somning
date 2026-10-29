namespace Somning.Core.Overlay;

/// <summary>
/// Software shader for the liquid glass orb. Writes premultiplied BGRA pixels (the layout UpdateLayeredWindow wants)
/// into a caller-owned buffer: deformed radial outline with analytic anti-aliasing, a volume gradient, liquid color
/// bands, a fresnel rim, an inner shadow, a bottom caustic, specular highlights and an outer glow. Pure and headless;
/// reuses its scratch buffers so a frame allocates nothing.
/// </summary>
public sealed class OrbRenderer
{
    private const float LightX = -0.6f;
    private const float LightY = -0.8f;
    private const float HighlightX = -0.34f;
    private const float HighlightY = -0.52f;
    private const float HighlightCos = 0.8139f; // rotation of the main highlight (-0.62 rad)
    private const float HighlightSin = -0.5810f;

    /// <summary>Above this DPI scale the orb is shaded at 96 dpi and upscaled.</summary>
    public const float MaxDirectDpiScale = 1.3f;

    private readonly OrbShape shape = new();
    private uint[] lowBuffer = [];
    private readonly float[] palette = new float[OrbPalette.Floats];

    public OrbShape Shape => shape;

    /// <summary>When false the orb is always shaded at the target resolution (previews, quality checks).</summary>
    public bool AllowUpscale { get; set; } = true;

    /// <summary>
    /// Renders one frame. <paramref name="plateWidth"/> &gt; 0 also draws the caption plate below the orb. Above
    /// <see cref="MaxDirectDpiScale"/> the orb is shaded at 96 dpi resolution and bilinearly upscaled (the glass is
    /// soft, so this is visually close and keeps the cost per frame bounded on HiDPI screens).
    /// </summary>
    public void Render(Span<uint> pixels, int width, int height, float dpiScale, in OrbFrame frame, int plateWidth = 0)
    {
        pixels[..(width * height)].Clear();
        if (frame.Opacity <= 0.002f) return;

        shape.Update(frame);
        OrbPalette.Resolve(frame.TranscribeMix, frame.NoticeMix, palette);

        var layout = OrbLayout.Scaled(dpiScale);
        if (dpiScale <= MaxDirectDpiScale || !AllowUpscale)
        {
            RenderOrb(pixels, width, height, width / 2f, layout.CenterY, layout.Radius, in frame);
        }
        else
        {
            RenderOrbUpscaled(pixels, width, height, dpiScale, layout, in frame);
        }

        if (plateWidth > 0)
            RenderPlate(pixels, width, height, width / 2f, layout.CaptionCenterY, plateWidth, layout.CaptionHeight, dpiScale, in frame);
    }

    private void RenderOrbUpscaled(Span<uint> pixels, int width, int height, float dpiScale, in OrbLayout layout, in OrbFrame frame)
    {
        var low = OrbLayout.Scaled(1f);
        var lowCount = low.Width * low.Height;
        if (lowBuffer.Length != lowCount) lowBuffer = new uint[lowCount];
        Array.Clear(lowBuffer);

        var reach = RenderOrb(lowBuffer, low.Width, low.Height, low.Width / 2f, low.CenterY, low.Radius, in frame);
        if (reach <= 0f) return;

        var ratio = 1f / dpiScale;          // destination pixel -> low-res pixel
        var cxDst = width / 2f;
        var half = reach * dpiScale + 2f;
        var x0 = Math.Max(0, (int)MathF.Floor(cxDst - half));
        var x1 = Math.Min(width - 1, (int)MathF.Ceiling(cxDst + half));
        var y0 = Math.Max(0, (int)MathF.Floor(layout.CenterY - half));
        var y1 = Math.Min(height - 1, (int)MathF.Ceiling(layout.CenterY + half));
        var lw = low.Width;
        var lh = low.Height;

        for (var y = y0; y <= y1; y++)
        {
            var sy = (y + 0.5f - layout.CenterY) * ratio + low.CenterY - 0.5f;
            var iy = (int)MathF.Floor(sy);
            var fy = sy - iy;
            var ya = Math.Clamp(iy, 0, lh - 1) * lw;
            var yb = Math.Clamp(iy + 1, 0, lh - 1) * lw;
            for (var x = x0; x <= x1; x++)
            {
                var sx = (x + 0.5f - cxDst) * ratio + lw / 2f - 0.5f;
                var ix = (int)MathF.Floor(sx);
                var fx = sx - ix;
                var xa = Math.Clamp(ix, 0, lw - 1);
                var xb = Math.Clamp(ix + 1, 0, lw - 1);
                pixels[y * width + x] = Bilinear(lowBuffer[ya + xa], lowBuffer[ya + xb], lowBuffer[yb + xa], lowBuffer[yb + xb], fx, fy);
            }
        }
    }

    private static uint Bilinear(uint p00, uint p10, uint p01, uint p11, float fx, float fy)
    {
        var w00 = (1f - fx) * (1f - fy);
        var w10 = fx * (1f - fy);
        var w01 = (1f - fx) * fy;
        var w11 = fx * fy;
        var a = (p00 >> 24) * w00 + (p10 >> 24) * w10 + (p01 >> 24) * w01 + (p11 >> 24) * w11;
        var r = ((p00 >> 16) & 0xFF) * w00 + ((p10 >> 16) & 0xFF) * w10 + ((p01 >> 16) & 0xFF) * w01 + ((p11 >> 16) & 0xFF) * w11;
        var g = ((p00 >> 8) & 0xFF) * w00 + ((p10 >> 8) & 0xFF) * w10 + ((p01 >> 8) & 0xFF) * w01 + ((p11 >> 8) & 0xFF) * w11;
        var b = (p00 & 0xFF) * w00 + (p10 & 0xFF) * w10 + (p01 & 0xFF) * w01 + (p11 & 0xFF) * w11;
        return ((uint)(a + 0.5f) << 24) | ((uint)(r + 0.5f) << 16) | ((uint)(g + 0.5f) << 8) | (uint)(b + 0.5f);
    }

    /// <summary>Shades the orb and its glow; returns the pixel radius of the touched circle (0 when nothing was drawn).</summary>
    private float RenderOrb(Span<uint> pixels, int width, int height, float cx, float cy, float baseRadius, in OrbFrame frame)
    {
        var radius = baseRadius * frame.Scale;
        if (radius < 0.5f) return 0f;

        var op = frame.Opacity;
        var level = frame.Level;
        var edgeWidth = 1f + frame.Soften * radius * 0.45f;
        var glowLength = radius * 0.17f;
        var invGlowLength = 1f / glowLength;
        var glowReach = radius * 0.55f;
        var glowStrength = 0.46f * (0.55f + 0.50f * level + 0.10f * frame.Breath) * op;
        var invRadius = 1f / radius;
        var angleToIndex = OrbShape.Samples / (2f * MathF.PI);
        var reach = radius * shape.MaxRadius + glowReach + edgeWidth;
        var tMix = frame.TranscribeMix;
        var vivid = 1.15f + 0.40f * level;
        var flow = frame.Flow;
        var swirl = frame.Swirl;

        var maxReach2 = reach * reach;
        var x0 = Math.Max(0, (int)MathF.Floor(cx - reach));
        var x1 = Math.Min(width - 1, (int)MathF.Ceiling(cx + reach));
        var y0 = Math.Max(0, (int)MathF.Floor(cy - reach));
        var y1 = Math.Min(height - 1, (int)MathF.Ceiling(cy + reach));

        var c0r = palette[0]; var c0g = palette[1]; var c0b = palette[2];
        var c1r = palette[3]; var c1g = palette[4]; var c1b = palette[5];
        var c2r = palette[6]; var c2g = palette[7]; var c2b = palette[8];
        var c3r = palette[9]; var c3g = palette[10]; var c3b = palette[11];
        var glr = palette[12]; var glg = palette[13]; var glb = palette[14];

        for (var y = y0; y <= y1; y++)
        {
            var dy = y + 0.5f - cy;
            var row = y * width;
            for (var x = x0; x <= x1; x++)
            {
                var dx = x + 0.5f - cx;
                var r2 = dx * dx + dy * dy;
                if (r2 > maxReach2) continue;
                var r = MathF.Sqrt(r2);
                var ang = OrbMath.Atan2(dy, dx);
                var edge = radius * shape.Lerp((ang + MathF.PI) * angleToIndex);
                var dist = r - edge;
                if (dist > glowReach + edgeWidth) continue;

                var glowA = 0f;
                if (dist > -edgeWidth)
                    glowA = glowStrength * Math.Max(0f, FastExpNeg(Math.Max(dist, 0f) * invGlowLength) - 0.04f) * 1.04f;

                if (dist >= edgeWidth * 0.5f)
                {
                    if (glowA < 0.002f) continue;
                    pixels[row + x] = Pack(glr * glowA, glg * glowA, glb * glowA, glowA);
                    continue;
                }

                var coverage = OrbMath.Clamp01(0.5f - dist / edgeWidth);
                var inv = 1f / edge;
                var t = Math.Min(1f, r * inv);
                var pnx = dx * inv;
                var pny = dy * inv;
                var z = MathF.Sqrt(Math.Max(0f, 1f - t * t));

                // Liquid color bands (domain warped sines); transcribing swaps the third band for a spiral.
                var px = dx * invRadius;
                var py = dy * invRadius;
                var a = px * 0.9f + py * 0.5f;
                var b = py - px * 0.4f;
                var s0 = a * 3.2f + flow + 1.3f * OrbMath.Sin(b * 2.1f - flow * 0.7f);
                var s1 = b * 3.6f - flow * 1.3f + 1.1f * OrbMath.Sin(a * 2.4f + flow * 0.6f);
                var s2 = (a - b) * 2.7f + flow * 0.8f + OrbMath.Sin(t * 4f - flow);
                var spiral = 2f * ang + 4f * t - swirl * 1.7f;
                s2 += (spiral - s2) * tMix;

                var w0 = 0.5f + 0.5f * OrbMath.Sin(s0);
                var w1 = 0.5f + 0.5f * OrbMath.Sin(s1);
                var w2 = 0.5f + 0.5f * OrbMath.Sin(s2);
                w0 = w0 * w0 * w0 + 0.03f;
                w1 = w1 * w1 * w1 + 0.03f;
                w2 = w2 * w2 * w2 + 0.03f;
                var norm = 1f / (w0 + w1 + w2 + 0.30f);
                var cr = (c0r * w0 + c1r * w1 + c2r * w2 + c3r * 0.30f) * norm;
                var cg = (c0g * w0 + c1g * w1 + c2g * w2 + c3g * 0.30f) * norm;
                var cb = (c0b * w0 + c1b * w1 + c2b * w2 + c3b * 0.30f) * norm;
                var gray = 0.30f * cr + 0.59f * cg + 0.11f * cb;
                cr = gray + (cr - gray) * vivid;
                cg = gray + (cg - gray) * vivid;
                cb = gray + (cb - gray) * vivid;

                // Glass shading.
                var fres = 1f - z;
                var fres3 = fres * fres * fres;
                var ndl = OrbMath.Clamp01(pnx * LightX + pny * LightY);
                var rim = fres3 * (0.35f + 1.1f * ndl);
                var body = 0.86f + 0.26f * z;
                var low = OrbMath.Clamp01(pny * 0.85f + 0.25f);
                var innerShadow = 1f - 0.34f * OrbMath.SmoothStep(0.45f, 1f, t) * low;
                var cd = (t - 0.80f) * 10f;
                var caustic = OrbMath.Clamp01(pny * 0.7f + pnx * 0.5f) * 0.55f / ((1f + cd * cd) * (1f + cd * cd));

                var qx = pnx - HighlightX;
                var qy = pny - HighlightY;
                var hu = (qx * HighlightCos + qy * HighlightSin) * (1f / 0.34f);
                var hv = (qy * HighlightCos - qx * HighlightSin) * (1f / 0.14f);
                var h1 = Math.Max(0f, 1f - (hu * hu + hv * hv));
                h1 = h1 * h1 * (3f - 2f * h1) * 0.85f;
                var sx = (pnx - 0.40f) * (1f / 0.16f);
                var sy = (pny - 0.50f) * (1f / 0.07f);
                var h2 = Math.Max(0f, 1f - (sx * sx + sy * sy));
                h2 = h2 * h2 * 0.32f;

                var light = rim + caustic;
                var spec = h1 + h2;
                var rr = Math.Min(1f, cr * body * innerShadow + c3r * light + spec);
                var rg = Math.Min(1f, cg * body * innerShadow + c3g * light + spec);
                var rb = Math.Min(1f, cb * body * innerShadow + c3b * light + spec);
                var alpha = Math.Min(1f, 0.60f + 0.25f * fres + 0.5f * (light + spec));

                var orbA = alpha * coverage * op;
                var through = 1f - orbA;
                var outA = orbA + glowA * through;
                pixels[row + x] = Pack(
                    rr * orbA + glr * glowA * through,
                    rg * orbA + glg * glowA * through,
                    rb * orbA + glb * glowA * through,
                    outA);
            }
        }

        return reach;
    }

    private void RenderPlate(Span<uint> pixels, int width, int height, float cx, float cy, int plateWidth, float plateHeight, float dpiScale, in OrbFrame frame)
    {
        var halfW = plateWidth / 2f;
        var halfH = plateHeight / 2f;
        var cap = halfH;
        var straight = Math.Max(0f, halfW - cap);
        var border = Math.Max(1f, 1.1f * dpiScale);
        var op = frame.Opacity;
        var n = frame.NoticeMix;

        // Dark glass fill; the border takes the mode accent (blue-ish normally, amber for notices).
        var fillA = (0.72f + 0.1f * n) * op;
        var fr = 0.06f; var fg = 0.07f; var fb = 0.10f;
        var br = 0.40f + 0.60f * n; var bg = 0.50f + 0.20f * n; var bb = 0.75f - 0.65f * n;

        var x0 = Math.Max(0, (int)MathF.Floor(cx - halfW - 1));
        var x1 = Math.Min(width - 1, (int)MathF.Ceiling(cx + halfW + 1));
        var y0 = Math.Max(0, (int)MathF.Floor(cy - halfH - 1));
        var y1 = Math.Min(height - 1, (int)MathF.Ceiling(cy + halfH + 1));

        for (var y = y0; y <= y1; y++)
        {
            var qy = y + 0.5f - cy;
            for (var x = x0; x <= x1; x++)
            {
                var qx = Math.Max(MathF.Abs(x + 0.5f - cx) - straight, 0f);
                var d = MathF.Sqrt(qx * qx + qy * qy) - cap;
                var coverage = OrbMath.Clamp01(0.5f - d);
                if (coverage <= 0f) continue;

                var inner = OrbMath.Clamp01(0.5f - (d + border));
                var edgeMix = 1f - inner;
                var rr = fr + (br - fr) * edgeMix * 0.7f;
                var rg = fg + (bg - fg) * edgeMix * 0.7f;
                var rb = fb + (bb - fb) * edgeMix * 0.7f;
                var a = (fillA + (0.6f * op - fillA) * edgeMix * 0.5f) * coverage;
                pixels[y * width + x] = Pack(rr * a, rg * a, rb * a, a);
            }
        }
    }

    // exp(-x) for x >= 0 via a rational approximation (error < 1%, plenty for a soft glow).
    private static float FastExpNeg(float x) => 1f / (1f + x + 0.5f * x * x + 0.1666667f * x * x * x);

    private static uint Pack(float r, float g, float b, float a)
    {
        var ia = (uint)(OrbMath.Clamp01(a) * 255f + 0.5f);
        var ir = Math.Min((uint)(OrbMath.Clamp01(r) * 255f + 0.5f), ia);
        var ig = Math.Min((uint)(OrbMath.Clamp01(g) * 255f + 0.5f), ia);
        var ib = Math.Min((uint)(OrbMath.Clamp01(b) * 255f + 0.5f), ia);
        return (ia << 24) | (ir << 16) | (ig << 8) | ib;
    }
}
