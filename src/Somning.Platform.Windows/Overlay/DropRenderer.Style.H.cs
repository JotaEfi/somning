using System;
using System.Runtime.CompilerServices;
using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

public sealed partial class DropRenderer
{
    private const float WaterCenterAlpha = 0.22f;
    private const float WaterBendCenter = 0.03f;
    private const float WaterBendRim = 2.4f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SampleWaterBackdrop(in Scene s, float x, float y, out float r, out float g, out float b)
    {
        if (s.BackdropMix > 0.001f && s.Tex is { } tex)
        {
            var tx = s.BackdropX0 + x * s.BackdropSx;
            var ty = s.BackdropY0 + y * s.BackdropSy;
            tex.SampleTexel(tx, ty, out r, out g, out b);
            return;
        }

        // Procedural world fallback when there's no desktop capture
        var py = Math.Clamp(0.5f + (y - s.CenterY) * 0.008f, 0f, 1f);
        r = 0.18f + 0.42f * py;
        g = 0.21f + 0.47f * py;
        b = 0.27f + 0.58f * py;
    }

    /// <summary>
    /// Style H ('Água escura'): clear water with semitransparent center, inverted perimeter refraction,
    /// reactive capillary wave rings on phoneme attacks, tone-guided specular reflection bands, internal caustic,
    /// and a deep dark water shadow ('sombra de água funda') undulating with the voice and rings.
    /// </summary>
    private Px ShadeGlassH(in Scene s, float dx, float dy, float dyl, float nx, float ny, float u)
    {
        var sx = dx * s.InvBoxW;
        var sy = dyl * s.InvBoxH;
        var u2 = u * u;
        var u4 = u2 * u2;
        var u8 = u4 * u4;

        // 1. One perturbed normal: voice swell + capillary onset wave rings + phoneme impacts
        var live = (1f - s.NoticeMix * 0.7f);
        var swell = (0.012f + 0.075f * s.VoiceEnergy) * live;
        var phase = s.F.Time * 2.8f + s.F.RipplePhase * 0.4f;
        var gx = swell * FastMath.Sin(sx * 3.4f + phase);
        var gy = 0.7f * swell * FastMath.Sin(sy * 3.8f + sx * 2.2f - phase * 0.8f);

        // Capillary onset rings driven by Wave0Front/Amp and Wave1Front/Amp
        var rDist = FastMath.Sqrt(sx * sx + sy * sy) + 1e-3f;
        if (s.F.Wave0Amp > 0.001f)
        {
            var r0 = 1f - s.F.Wave0Front;
            var dr0 = rDist - r0;
            var ring0 = s.F.Wave0Amp * 0.32f * FastMath.Cos(dr0 * 22f) / (1f + 32f * dr0 * dr0);
            gx += ring0 * (sx / rDist);
            gy += ring0 * (sy / rDist);
        }

        if (s.F.Wave1Amp > 0.001f)
        {
            var r1 = 1f - s.F.Wave1Front;
            var dr1 = rDist - r1;
            var ring1 = s.F.Wave1Amp * 0.28f * FastMath.Cos(dr1 * 26f) / (1f + 36f * dr1 * dr1);
            gx += ring1 * (sx / rDist);
            gy += ring1 * (sy / rDist);
        }

        var rimFade = 1f - 0.55f * u8;
        gx *= rimFade;
        gy *= rimFade;
        var pnx = nx + gx;
        var pny = ny + gy;
        var pn2 = Math.Min(1f, pnx * pnx + pny * pny);
        var pnz = FastMath.Sqrt(1f - pn2);

        // 2. Clear water transmission: sharp semitransparent center, inverted fold beyond rim
        var bend = WaterBendCenter + WaterBendRim * u8;
        var mapX = s.CenterX + dx - nx * s.RefractReach * bend - gx * s.HalfWidth * 0.15f;
        var mapY = s.CenterY + dy - ny * s.RefractReach * bend - gy * s.HalfHeight * 0.15f;
        SampleWaterBackdrop(in s, mapX, mapY, out var bgR, out var bgG, out var bgB);

        // Water absorption and capillary crest light concentration
        var crest = Math.Clamp(1f + 2.4f * (gx * 0.45f + gy * 0.60f), 0.50f, 1.30f);
        bgR *= crest * 0.94f;
        bgG *= crest * 0.97f;
        bgB *= crest * 1.00f;

        // Semitransparent body coverage
        var bent = FastMath.Smooth((u - 0.50f) / 0.45f);
        var coverage = Math.Clamp(WaterCenterAlpha + (1f - WaterCenterAlpha) * bent + 0.08f * s.VoiceEnergy, 0f, 0.96f);
        var px = new Px(bgR * coverage, bgG * coverage, bgB * coverage, coverage);

        // 3. MANCHA ESCURA CENTRAL ("Sombra de Água Funda") - Absolute Rules 1 & 2
        // Deep dark water shadow undulating with the capillary rings and voice waves
        var depth = 1f - FastMath.Smooth((u - 0.80f) / 0.20f) * 0.50f;
        var column = Math.Clamp((int)(s.CenterX + dx), 0, waveCenter.Length - 1);
        var waterUndulation = gy * 0.40f;
        var spread = 0.112f + 0.12f * s.VoiceEnergy;
        var d0 = (sy + waterUndulation - waveCenter[column]) / spread;
        var d1 = (sy + waterUndulation - waveUpper[column]) / (spread * 0.72f);
        var d2 = (sy + waterUndulation - waveLower[column]) / (spread * 0.65f);

        // Dark water shadow profile
        var band = 0.86f / (1f + 1.15f * d0 * d0) + 0.35f / (1f + 2.1f * d1 * d1) + 0.25f / (1f + 1.9f * d2 * d2);

        // 4. Transcribing liquid flow inside the water, absorbed by the dark deep core
        var transMix = s.TranscribeMix;
        var strength = 0f;
        var intensity = 0f;
        float liquidR = 0.14f, liquidG = 0.14f, liquidB = 0.14f;
        if (transMix > 0.005f || s.NoticeMix > 0.005f)
        {
            var lens = 1f - 0.28f * u2;
            var lx = sx * lens;
            var ly = sy * lens;
            var twist = transMix * 0.9f * (1f - u);
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
            var hot = Math.Clamp(intensity - 1.5f, 0f, 1f) * 0.12f;
            liquidR = Math.Min(1f, cr * invSum + hot);
            liquidG = Math.Min(1f, cg * invSum + hot);
            liquidB = Math.Min(1f, cb * invSum + hot);

            // Flowing currents inside the water (calm cyan / violet / amber)
            var liquidBandSuppress = 1f - 0.62f * Math.Clamp(band, 0f, 1f);
            var la = Math.Min(0.42f, strength * 0.48f) * liquidBandSuppress * (transMix + s.NoticeMix);
            px = Px.Over(new Px(liquidR * la, liquidG * la, liquidB * la, la), px);

            var bloom = strength * 0.038f * (1f - u2) * liquidBandSuppress * transMix;
            px = Px.Add(px, liquidR * bloom, liquidG * bloom, liquidB * bloom, bloom * 0.4f);
        }

        // Apply dark water center band (deep ocean black-blue shadow)
        var darkStrength = 1.05f + 0.38f * s.VoiceEnergy;
        var darkAlpha = Math.Clamp(band * depth * darkStrength, 0f, 0.96f);
        var darkGray = 0.008f;
        var darkPx = new Px(darkGray * darkAlpha, (darkGray + 0.002f) * darkAlpha, (darkGray + 0.006f) * darkAlpha, darkAlpha);
        px = Px.Over(darkPx, px);

        // Fine reactive lines: silver caustics undulating with capillary rings
        var seam = (sy + waterUndulation - waveCenter[column] + spread * 0.38f) / 0.018f;
        var silver = (0.13f + 0.52f * s.VoiceEnergy) / (1f + seam * seam) * depth;
        var seam2 = (sy + waterUndulation - waveUpper[column] - spread * 0.26f) / 0.015f;
        var silver2 = (0.08f + 0.30f * s.VoiceEnergy) / (1f + seam2 * seam2) * depth;
        var caustics = (silver + silver2) * (1f + 1.25f * s.Impact) * (1f - 0.25f * transMix);
        px = Px.Add(px, caustics * 0.92f, caustics * 0.95f, caustics * 1.05f, caustics * 0.65f);

        // 5. Fresnel reflection on the perturbed normal
        var f1 = 1f - pnz;
        var fresnel = 0.035f + 0.88f * f1 * f1 * f1;
        SampleWaterBackdrop(in s, s.CenterX + dx + pnx * s.EnvReach * 1.15f, s.CenterY + dy + pny * s.EnvReach * 1.15f, out var er, out var eg, out var eb);
        px = Px.Add(px, er * fresnel * 0.92f, eg * fresnel * 0.95f, eb * fresnel * 1.05f, fresnel * 0.85f);

        // 6. Tone-guided specular reflection (grave = wide low band, agudo = sharp high scratch)
        var gloss = 0f;
        if (s.VoiceEnergy > 0.01f || s.Impact > 0.01f)
        {
            var toneCenter = 0.28f - 0.92f * s.Tone;
            var toneHalfWidth = 0.24f - 0.20f * s.Tone;
            var v = (sy + gy * 2.0f - toneCenter * (1f - 0.35f * sx * sx)) / toneHalfWidth;
            var toneBand = 1f / (1f + v * v * v * v);
            var toneSide = MathF.Max(0f, 1f - sx * sx * 1.15f);
            var toneStrength = (0.16f + 0.22f * s.Tone) * (s.VoiceEnergy + 1.25f * s.Impact);
            gloss += toneBand * toneSide * toneStrength * (1f - 0.70f * u4);
        }

        // 7. Internal water caustics focused inside the lower rim
        var below = MathF.Max(0f, 0.34f * nx + 0.94f * ny);
        if (below > 0.01f && u > 0.45f)
        {
            var caustBand = FastMath.Smooth((u - 0.48f) / 0.28f) * (1f - FastMath.Smooth((u - 0.80f) / 0.15f));
            var breakup = Math.Clamp(1f + 4.5f * (gx + gy), 0.2f, 2.2f);
            var causticLight = caustBand * below * below * breakup * (0.08f + 0.20f * s.VoiceEnergy + 0.65f * s.Impact);
            gloss += causticLight;
        }

        // Key glint specular
        var sp = MathF.Max(0f, -0.46f * pnx - 0.62f * pny + 0.63f * pnz);
        var sp2 = sp * sp;
        var sp4 = sp2 * sp2;
        var sp8 = sp4 * sp4;
        var sp32 = sp8 * sp8 * sp8 * sp8;
        gloss += 0.10f * sp8 * sp4 + 0.92f * sp32 * sp32;

        // 8. Meniscus contour & edge holding for clear/white desktop contrast
        var meniscus = FastMath.Smooth((u - 0.920f) / 0.045f) * (1f - FastMath.Smooth((u - 0.978f) / 0.015f));
        px = Px.Over(new Px(0f, 0f, 0f, 0.38f * meniscus), px);

        var outline = FastMath.Smooth((u - 0.965f) / 0.035f);
        px = Px.Over(new Px(0f, 0f, 0f, 0.45f * outline), px);

        gloss = Math.Min(1f, gloss) * s.GlossScale;
        return Px.Add(px, gloss, gloss, gloss, gloss * 0.90f);
    }
}
