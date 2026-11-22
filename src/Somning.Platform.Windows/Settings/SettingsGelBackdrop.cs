using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Settings;

/// <summary>
/// Liquid-glass state driving the dark frosted-gel backdrop (drag inertia tilt / slosh).
/// </summary>
public readonly record struct GelBackdropState(float TiltX, float TiltY)
{
    public static GelBackdropState Neutral => default;
}

/// <summary>
/// Renders a dark, translucent liquid-glass ("gel reativo") surface with crisp 22 px SDF rounded corners,
/// 3D meniscus refraction, subtle chromatic dispersion, Blinn-Phong specular highlights, and drag/cursor reactivity.
/// </summary>
public static class SettingsGelBackdrop
{
    private const int BlurRadius = 5;
    private const float BlurSigma = 2.2f;
    private const float AreaTexelDip = 2.5f;
    private const int MaxAreaTexelsPerSide = 1280;
    private const float RenderScale = 0.5f; // half-res for ~1.5 ms frames; WPF vector Clip keeps 22 px edges razor-sharp
    private static readonly float[] BlurKernel = BuildGaussianKernel(BlurRadius, BlurSigma);

    /// <summary>
    /// Captures <paramref name="area"/> (device pixels) once before the window is visible, applies a separable
    /// Gaussian blur in parallel, and returns the reduced texture so move/resize/reactive frames never recapture.
    /// </summary>
    public static BackdropTexture? CaptureBlurredArea(IBackdropSource source, in BackdropRegion area, float dpiScale)
    {
        if (area.IsEmpty) return null;
        if (dpiScale <= 0f) dpiScale = 1f;
        var texelPx = AreaTexelDip * dpiScale;
        var tw = Math.Clamp((int)MathF.Ceiling(area.Width / texelPx), 2, MaxAreaTexelsPerSide);
        var th = Math.Clamp((int)MathF.Ceiling(area.Height / texelPx), 2, MaxAreaTexelsPerSide);
        var captured = new BackdropTexture(tw, th)
        {
            OriginX = area.Left,
            OriginY = area.Top,
            PixelsPerTexelX = (float)area.Width / tw,
            PixelsPerTexelY = (float)area.Height / th
        };
        if (!source.TryCapture(area, captured)) return null;

        var blurred = new BackdropTexture(tw, th)
        {
            OriginX = captured.OriginX,
            OriginY = captured.OriginY,
            PixelsPerTexelX = captured.PixelsPerTexelX,
            PixelsPerTexelY = captured.PixelsPerTexelY
        };
        BlurInto(captured.Data, blurred.Data, tw, th);
        return blurred;
    }

    /// <summary>
    /// Renders the reactive liquid-glass bitmap into a reusable <see cref="WriteableBitmap"/> (half resolution for
    /// instant, zero-lag updates during fast drags and 60 fps reactivity).
    /// </summary>
    public static WriteableBitmap RenderInto(
        WriteableBitmap? existing,
        BackdropTexture? blurredArea,
        in BackdropRegion windowBounds,
        float dpiScale,
        float cornerRadiusDip,
        in GelBackdropState state,
        ref byte[]? scratchBuffer,
        float renderScale = RenderScale)
    {
        if (windowBounds.IsEmpty)
            throw new ArgumentException("Window bounds cannot be empty.", nameof(windowBounds));
        if (dpiScale <= 0f) dpiScale = 1f;
        var scale = Math.Clamp(renderScale, 0.25f, 1f);

        var width = Math.Max(2, (int)MathF.Round(windowBounds.Width * scale));
        var height = Math.Max(2, (int)MathF.Round(windowBounds.Height * scale));
        var byteCount = width * height * 4;
        if (scratchBuffer is null || scratchBuffer.Length < byteCount)
            scratchBuffer = new byte[byteCount];

        RenderPixels(scratchBuffer, width, height, blurredArea, windowBounds, dpiScale, cornerRadiusDip, state);

        var bitmap = existing is not null && existing.PixelWidth == width && existing.PixelHeight == height
            ? existing
            : new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), scratchBuffer, width * 4, 0);
        return bitmap;
    }

    /// <summary>Renders a full-resolution frozen <see cref="BitmapSource"/> for off-screen tests and previews.</summary>
    public static BitmapSource RenderGel(
        BackdropTexture? blurredArea,
        in BackdropRegion windowBounds,
        float dpiScale = 1f,
        float cornerRadiusDip = 22f,
        in GelBackdropState state = default)
    {
        byte[]? buffer = null;
        var wb = RenderInto(null, blurredArea, windowBounds, dpiScale, cornerRadiusDip, state, ref buffer, renderScale: 1f);
        wb.Freeze();
        return wb;
    }

    private static void RenderPixels(
        byte[] pixels,
        int width,
        int height,
        BackdropTexture? blurredArea,
        in BackdropRegion windowBounds,
        float dpiScale,
        float cornerRadiusDip,
        in GelBackdropState state)
    {
        var scaleX = (float)width / windowBounds.Width;
        var scaleY = (float)height / windowBounds.Height;
        var dipToBufX = dpiScale * scaleX;
        var dipToBufY = dpiScale * scaleY;
        var dipToBuf = 0.5f * (dipToBufX + dipToBufY);

        var radiusPx = Math.Clamp(cornerRadiusDip * dipToBuf, 0f, Math.Min(width, height) * 0.5f);
        var halfW = width * 0.5f;
        var halfH = height * 0.5f;
        var innerHalfW = Math.Max(0f, halfW - radiusPx);
        var innerHalfH = Math.Max(0f, halfH - radiusPx);
        var invW = 1f / Math.Max(1, width - 1);
        var invH = 1f / Math.Max(1, height - 1);

        var hasBackdrop = blurredArea is not null;
        var data = blurredArea?.Data;
        var bw = blurredArea?.Width ?? 0;
        var bh = blurredArea?.Height ?? 0;
        var relOriginX = hasBackdrop ? blurredArea!.OriginX - windowBounds.Left : 0f;
        var relOriginY = hasBackdrop ? blurredArea!.OriginY - windowBounds.Top : 0f;
        var bufToWinX = (float)windowBounds.Width / width;
        var bufToWinY = (float)windowBounds.Height / height;
        var invPptX = hasBackdrop && blurredArea!.PixelsPerTexelX > 0f ? 1f / blurredArea.PixelsPerTexelX : 0f;
        var invPptY = hasBackdrop && blurredArea!.PixelsPerTexelY > 0f ? 1f / blurredArea.PixelsPerTexelY : 0f;

        // Meniscus (curved glass rim) geometry.
        var meniscusPx = 28f * dipToBuf;
        var rimRefractWinPx = 11f * dpiScale;
        var chromaWinPx = 2.2f * dpiScale;

        // Studio key light direction tilted by drag inertia (slosh).
        var tiltX = Math.Clamp(state.TiltX, -1f, 1f);
        var tiltY = Math.Clamp(state.TiltY, -1f, 1f);
        var keyX = -0.28f + tiltX * 0.45f;
        var keyY = -0.68f + tiltY * 0.45f;
        var keyZ = 0.68f;
        var keyLen = MathF.Sqrt(keyX * keyX + keyY * keyY + keyZ * keyZ);
        keyX /= keyLen;
        keyY /= keyLen;
        keyZ /= keyLen;

        // Halfway vector for Blinn-Phong specular with view vector V = (0, 0, 1).
        var halfKeyX = keyX;
        var halfKeyY = keyY;
        var halfKeyZ = keyZ + 1f;
        var halfKeyLen = MathF.Sqrt(halfKeyX * halfKeyX + halfKeyY * halfKeyY + halfKeyZ * halfKeyZ);
        halfKeyX /= halfKeyLen;
        halfKeyY /= halfKeyLen;
        halfKeyZ /= halfKeyLen;

        Parallel.For(0, height, y =>
        {
            var tY = y * invH;
            SampleObsidianGelVeil(tY, out var veilR, out var veilG, out var veilB, out var veilA);

            var py = y + 0.5f - halfH;
            var absPy = MathF.Abs(py);
            var qy = absPy - innerHalfH;
            var rowOffset = y * width * 4;

            for (var x = 0; x < width; x++)
            {
                var px = x + 0.5f - halfW;
                var absPx = MathF.Abs(px);
                var qx = absPx - innerHalfW;

                var outX = MathF.Max(qx, 0f);
                var outY = MathF.Max(qy, 0f);
                var outside = MathF.Sqrt(outX * outX + outY * outY);
                var inside = MathF.Min(MathF.Max(qx, qy), 0f);
                var sdf = outside + inside - radiusPx;

                var cov = Math.Clamp(0.5f - sdf, 0f, 1f);
                var idx = rowOffset + x * 4;
                if (cov <= 0f)
                {
                    pixels[idx] = 0;
                    pixels[idx + 1] = 0;
                    pixels[idx + 2] = 0;
                    pixels[idx + 3] = 0;
                    continue;
                }

                // 3D droplet surface normal: gentle convex dome + pronounced meniscus at the 22 px rim.
                var uX = x * invW * 2f - 1f;
                var uY = tY * 2f - 1f;
                var nx = uX * 0.08f + tiltX * 0.06f;
                var ny = uY * 0.08f + tiltY * 0.06f;

                var edgeDist = MathF.Max(0f, -sdf);
                var rimMask = edgeDist < meniscusPx ? SmoothStep(1f - edgeDist / meniscusPx) : 0f;
                if (rimMask > 0.001f)
                {
                    var rnx = outX > 0f || outY > 0f
                        ? (px >= 0f ? outX : -outX) / MathF.Max(outside, 0.001f)
                        : (qx >= qy ? (px >= 0f ? 1f : -1f) : 0f);
                    var rny = outX > 0f || outY > 0f
                        ? (py >= 0f ? outY : -outY) / MathF.Max(outside, 0.001f)
                        : (qy > qx ? (py >= 0f ? 1f : -1f) : 0f);
                    var bevel = rimMask * rimMask * 0.62f;
                    nx += rnx * bevel;
                    ny += rny * bevel;
                }

                // Normalize 3D surface normal N = (nx, ny, nz).
                var nxySq = nx * nx + ny * ny;
                var nz = nxySq < 0.96f ? MathF.Sqrt(1f - nxySq) : 0.2f;
                var invNLen = 1f / MathF.Sqrt(nxySq + nz * nz);
                nx *= invNLen;
                ny *= invNLen;
                nz *= invNLen;

                // Fresnel factor (stronger glass reflection & cyan edge tint at grazing angles).
                var fresnel = 1f - nz;
                var fresnel2 = fresnel * fresnel;

                // Blinn-Phong studio specular highlight.
                var nDotH = MathF.Max(0f, nx * halfKeyX + ny * halfKeyY + nz * halfKeyZ);
                var specKey = Pow16(nDotH) * (0.14f + rimMask * 0.32f);

                float r, g, b, outA;
                if (hasBackdrop)
                {
                    var winX = (x + 0.5f) * bufToWinX;
                    var winY = (y + 0.5f) * bufToWinY;
                    var refrX = -nx * rimRefractWinPx;
                    var refrY = -ny * rimRefractWinPx;

                    // Subtle chromatic dispersion along the curved glass rim.
                    var dispX = -nx * chromaWinPx * rimMask;
                    var dispY = -ny * chromaWinPx * rimMask;

                    float bgR, bgG, bgB;
                    if (MathF.Abs(dispX) + MathF.Abs(dispY) > 0.25f)
                    {
                        var sxR = (winX + refrX + dispX - relOriginX) * invPptX - 0.5f;
                        var syR = (winY + refrY + dispY - relOriginY) * invPptY - 0.5f;
                        var sxG = (winX + refrX - relOriginX) * invPptX - 0.5f;
                        var syG = (winY + refrY - relOriginY) * invPptY - 0.5f;
                        var sxB = (winX + refrX - dispX - relOriginX) * invPptX - 0.5f;
                        var syB = (winY + refrY - dispY - relOriginY) * invPptY - 0.5f;
                        SampleBilinear(data!, bw, bh, sxR, syR, out bgR, out _, out _);
                        SampleBilinear(data!, bw, bh, sxG, syG, out _, out bgG, out _);
                        SampleBilinear(data!, bw, bh, sxB, syB, out _, out _, out bgB);
                    }
                    else
                    {
                        var sx = (winX + refrX - relOriginX) * invPptX - 0.5f;
                        var sy = (winY + refrY - relOriginY) * invPptY - 0.5f;
                        SampleBilinear(data!, bw, bh, sx, sy, out bgR, out bgG, out bgB);
                    }

                    // Adaptive dark smoked-gel absorption: deep & readable on pure white, luminous on dark/color.
                    var luma = bgR * 0.299f + bgG * 0.587f + bgB * 0.114f;
                    var adaptVeil = Math.Clamp(veilA + MathF.Max(0f, luma - 0.35f) * 0.28f, 0f, 0.90f);
                    var invVeil = 1f - adaptVeil;
                    r = bgR * invVeil + veilR * adaptVeil;
                    g = bgG * invVeil + veilG * adaptVeil;
                    b = bgB * invVeil + veilB * adaptVeil;
                    outA = cov;
                }
                else
                {
                    r = veilR;
                    g = veilG;
                    b = veilB;
                    outA = cov * MathF.Max(veilA, 0.88f);
                }

                // Liquid-glass Fresnel meniscus sheen and glossy droplet speculars.
                var fresnelSheen = fresnel2 * 0.28f;

                r = Math.Clamp(r + fresnelSheen * 0.48f + specKey * 0.86f, 0f, 1f);
                g = Math.Clamp(g + fresnelSheen * 0.88f + specKey * 0.96f, 0f, 1f);
                b = Math.Clamp(b + fresnelSheen * 1.00f + specKey * 1.00f, 0f, 1f);

                // Premultiplied BGRA (Pbgra32).
                pixels[idx] = ToByte(b * outA);
                pixels[idx + 1] = ToByte(g * outA);
                pixels[idx + 2] = ToByte(r * outA);
                pixels[idx + 3] = ToByte(outA);
            }
        });
    }

    private static void SampleObsidianGelVeil(float t, out float r, out float g, out float b, out float a)
    {
        if (t <= 0.45f)
        {
            var u = t / 0.45f;
            r = Lerp(0x0D / 255f, 0x09 / 255f, u);
            g = Lerp(0x13 / 255f, 0x0D / 255f, u);
            b = Lerp(0x1B / 255f, 0x14 / 255f, u);
            a = Lerp(0.68f, 0.64f, u);
        }
        else
        {
            var u = (t - 0.45f) / 0.55f;
            r = Lerp(0x09 / 255f, 0x05 / 255f, u);
            g = Lerp(0x0D / 255f, 0x08 / 255f, u);
            b = Lerp(0x14 / 255f, 0x0C / 255f, u);
            a = Lerp(0.64f, 0.74f, u);
        }
    }

    private static float Pow16(float x)
    {
        var x2 = x * x;
        var x4 = x2 * x2;
        var x8 = x4 * x4;
        return x8 * x8;
    }

    private static void BlurInto(float[] src, float[] dst, int width, int height)
    {
        var temp = new float[src.Length];
        var kernel = BlurKernel;

        Parallel.For(0, height, y =>
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                float r = 0f, g = 0f, b = 0f;
                for (var k = -BlurRadius; k <= BlurRadius; k++)
                {
                    var sx = Math.Clamp(x + k, 0, width - 1);
                    var w = kernel[k + BlurRadius];
                    var idx = (row + sx) * 3;
                    r += src[idx] * w;
                    g += src[idx + 1] * w;
                    b += src[idx + 2] * w;
                }
                var outIdx = (row + x) * 3;
                temp[outIdx] = r;
                temp[outIdx + 1] = g;
                temp[outIdx + 2] = b;
            }
        });

        Parallel.For(0, height, y =>
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                float r = 0f, g = 0f, b = 0f;
                for (var k = -BlurRadius; k <= BlurRadius; k++)
                {
                    var sy = Math.Clamp(y + k, 0, height - 1);
                    var w = kernel[k + BlurRadius];
                    var idx = (sy * width + x) * 3;
                    r += temp[idx] * w;
                    g += temp[idx + 1] * w;
                    b += temp[idx + 2] * w;
                }
                var outIdx = (row + x) * 3;
                dst[outIdx] = r;
                dst[outIdx + 1] = g;
                dst[outIdx + 2] = b;
            }
        });
    }

    private static void SampleBilinear(float[] data, int width, int height, float x, float y, out float r, out float g, out float b)
    {
        var cx = Math.Clamp(x, 0f, width - 1);
        var cy = Math.Clamp(y, 0f, height - 1);
        var x0 = (int)cx;
        var y0 = (int)cy;
        var x1 = Math.Min(x0 + 1, width - 1);
        var y1 = Math.Min(y0 + 1, height - 1);
        var fx = cx - x0;
        var fy = cy - y0;

        var i00 = (y0 * width + x0) * 3;
        var i10 = (y0 * width + x1) * 3;
        var i01 = (y1 * width + x0) * 3;
        var i11 = (y1 * width + x1) * 3;

        var w00 = (1f - fx) * (1f - fy);
        var w10 = fx * (1f - fy);
        var w01 = (1f - fx) * fy;
        var w11 = fx * fy;

        r = data[i00] * w00 + data[i10] * w10 + data[i01] * w01 + data[i11] * w11;
        g = data[i00 + 1] * w00 + data[i10 + 1] * w10 + data[i01 + 1] * w01 + data[i11 + 1] * w11;
        b = data[i00 + 2] * w00 + data[i10 + 2] * w10 + data[i01 + 2] * w01 + data[i11 + 2] * w11;
    }

    private static float[] BuildGaussianKernel(int radius, float sigma)
    {
        var kernel = new float[radius * 2 + 1];
        var sum = 0f;
        var denom = 2f * sigma * sigma;
        for (var i = -radius; i <= radius; i++)
        {
            var value = MathF.Exp(-(i * i) / denom);
            kernel[i + radius] = value;
            sum += value;
        }
        for (var i = 0; i < kernel.Length; i++) kernel[i] /= sum;
        return kernel;
    }

    private static float SmoothStep(float t)
    {
        var c = Math.Clamp(t, 0f, 1f);
        return c * c * (3f - 2f * c);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
