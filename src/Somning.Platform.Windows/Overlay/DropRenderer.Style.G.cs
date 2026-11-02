using System.Runtime.CompilerServices;
using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

public sealed partial class DropRenderer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SampleBackdropColor(in Scene s, float x, float y, out float r, out float g, out float b)
    {
        if (s.Tex is null)
        {
            r = g = b = 0f;
            return;
        }

        s.Tex.SampleTexel(s.BackdropX0 + x * s.BackdropSx, s.BackdropY0 + y * s.BackdropSy, out r, out g, out b);
    }

    private Px ShadeGlassG(in Scene s, float dx, float dy, float dyl, float nx, float ny, float u)
    {
        var u2 = u * u;
        var nz = FastMath.Sqrt(MathF.Max(0f, 1f - u2));
        var rim1 = 1f - nz;
        var mix = s.BackdropMix;

        // Sphere-space coordinates normalized to the drop's bounding box
        var sx = dx * s.InvBoxW;
        var sy = dyl * s.InvBoxH;

        // Radial direction
        var uiNorm = u > 1e-4f ? 1f / u : 0f;
        var zx = nx * uiNorm;
        var zy = ny * uiNorm;

        // 1. Refraction: gravitational liquid lens with central magnification (~1.39x lupa),
        // peripheral ring compression, anisotropic oval curvature and acoustic micro-vibrations
        float refR = 0f, refG = 0f, refB = 0f;
        var px = default(Px);
        if (mix > 0f)
        {
            var mag = 0.72f + 0.16f * u2;
            var compress = s.RefractReach * (1.15f * u2 * u - 0.72f * u2 * u2);
            var acoustic = s.VoiceEnergy > 0.005f
                ? s.RefractReach * (0.038f * FastMath.Sin(u * 14f - s.F.RipplePhase + s.F.Tone * 3.5f) + 0.018f * FastMath.Sin(u * 25f + s.F.RipplePhase * 1.2f))
                : 0f;
            var disp = compress + acoustic;
            var sampleX = s.CenterX + dx * mag + nx * disp;
            var sampleY = s.CenterY + dy * mag + ny * disp;
            SampleBackdropColor(in s, sampleX, sampleY, out refR, out refG, out refB);
            var lensA = mix * (0.88f + 0.10f * u2);
            px = new Px(refR * lensA, refG * lensA, refB * lensA, lensA);
        }

        // 2. Glass body
        var lit = MathF.Max(0f, -0.45f * nx - 0.60f * ny + 0.66f * nz);
        var bodyA = (0.05f + 0.28f * rim1 * rim1) * (1f - 0.65f * mix) + 0.58f * s.NoticeMix;
        var bodyGray = 0.015f + 0.08f * lit;
        var body = new Px(bodyGray * bodyA, bodyGray * bodyA, bodyGray * bodyA, bodyA);
        px = Px.Over(body, px);

        // 3. MANCHA ESCURA CENTRAL E ONDAS REATIVAS (Regras Absolutas 1 e 2)
        // Deep lens depth shadow + graphite wave ribbons with fine silver/caustic seams
        var depth = 1f - FastMath.Smooth((u - 0.80f) / 0.2f) * 0.55f;
        var column = Math.Clamp((int)(s.CenterX + dx), 0, waveCenter.Length - 1);
        var spread = 0.115f + 0.12f * s.VoiceEnergy;
        var d0 = (sy - waveCenter[column]) / spread;
        var d1 = (sy - waveUpper[column]) / (spread * 0.75f);
        var d2 = (sy - waveLower[column]) / (spread * 0.65f);

        // Wave profile forming the dark core
        var band = 0.84f / (1f + 1.1f * d0 * d0) + 0.35f / (1f + 2.0f * d1 * d1) + 0.25f / (1f + 1.8f * d2 * d2);

        // 4. Transcribing liquid flow (blobs) inside the bubble, absorbed by the dark core
        var transMix = s.TranscribeMix;
        var strength = 0f;
        var intensity = 0f;
        float liquidR = 0.14f, liquidG = 0.14f, liquidB = 0.14f;
        if (transMix > 0.005f || s.NoticeMix > 0.005f)
        {
            var lens = 1f - 0.30f * u2;
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

            // Subtle liquid inside the drop (calm cyan / violet / amber)
            var liquidBandSuppress = 1f - 0.62f * Math.Clamp(band, 0f, 1f);
            var la = Math.Min(0.42f, strength * 0.50f) * liquidBandSuppress * (transMix + s.NoticeMix);
            px = Px.Over(new Px(liquidR * la, liquidG * la, liquidB * la, la), px);

            var bloom = strength * 0.04f * (1f - u2) * liquidBandSuppress * transMix;
            px = Px.Add(px, liquidR * bloom, liquidG * bloom, liquidB * bloom, bloom * 0.4f);
        }

        // Apply dark center band (both listening and transcribing preserve the dark center band - Rule 2)
        var darkStrength = 1.05f + 0.35f * s.VoiceEnergy;
        var darkAlpha = Math.Clamp(band * depth * darkStrength, 0f, 0.96f);
        var darkGray = 0.010f;
        var darkPx = new Px(darkGray * darkAlpha, darkGray * darkAlpha, darkGray * darkAlpha, darkAlpha);
        px = Px.Over(darkPx, px);

        // Fine reactive lines: silver caustic seam and upper acoustic contour inside the dark band
        var seam = (sy - waveCenter[column] + spread * 0.40f) / 0.020f;
        var silver = 0.18f / (1f + seam * seam) * depth;
        var seam2 = (sy - waveUpper[column] - spread * 0.28f) / 0.016f;
        var silver2 = 0.10f / (1f + seam2 * seam2) * depth;
        var caustics = (silver + silver2) * (1f + 1.25f * s.Impact) * (1f - 0.25f * transMix);
        px = Px.Add(px, caustics * 0.94f, caustics * 0.96f, caustics * 1.05f, caustics * 0.65f);

        // 5. Inner Occlusion & Secondary rim bounce
        var toward = MathF.Max(0f, nx * 0.55f + ny * 0.83f);
        var wall = FastMath.Smooth((u - 0.74f) / 0.20f) * (1f - FastMath.Smooth((u - 0.96f) / 0.035f));
        var occlusion = wall * 0.24f * (1f - toward) * (1f - 0.5f * s.NoticeMix);
        if (occlusion > 0.001f)
        {
            px = Px.Over(new Px(0f, 0f, 0f, occlusion), px);
        }

        var ring = FastMath.Smooth((u - 0.58f) / 0.30f) * (1f - FastMath.Smooth((u - 0.90f) / 0.09f));
        var crescent = ring * toward * toward * 0.58f * (1f - 0.6f * s.NoticeMix);
        px = Px.Add(px, (0.55f * liquidR + 0.45f) * crescent, (0.55f * liquidG + 0.45f) * crescent, (0.55f * liquidB + 0.45f) * crescent, crescent * 0.7f);

        // 6. Fresnel rim & dispersion
        var warm = 0.5f + 0.5f * (nx * 0.6f + ny * 0.8f);
        var fresR = Fresnel(u * 0.98f);
        var fresG = Fresnel(u * 0.99f);
        var fresB = Fresnel(u);
        var baseR = 0.90f;
        var baseG = 0.90f;
        var baseB = 0.92f;
        if (mix > 0f && u > 0.78f)
        {
            var ui = 1f / MathF.Max(u, 0.05f);
            var ox = nx * ui;
            var oy = ny * ui;
            var envReach = s.EnvReach;
            SampleBackdropColor(in s, s.CenterX + dx * 1.1f + ox * envReach * 0.94f, s.CenterY + dy * 1.1f + oy * envReach * 0.94f, out var aR, out var aG, out _);
            SampleBackdropColor(in s, s.CenterX + dx * 1.1f + ox * envReach * 1.06f, s.CenterY + dy * 1.1f + oy * envReach * 1.06f, out _, out var bG, out var bB);
            var kEnv = 0.72f * mix;
            baseR += (aR * 1.25f + 0.10f - baseR) * kEnv;
            baseG += ((aG + bG) * 0.625f + 0.10f - baseG) * kEnv;
            baseB += (bB * 1.25f + 0.10f - baseB) * kEnv;
        }

        px = Px.Add(px, baseR * fresR * 0.95f, baseG * fresG * 0.95f, baseB * fresB * 0.95f, Math.Max(fresR, Math.Max(fresG, fresB)) * 0.90f);

        // Second rim light: warm bounce hugging the lower-right edge
        var bounceBand = FastMath.Smooth((u - 0.82f) / 0.10f) * (1f - FastMath.Smooth((u - 0.965f) / 0.03f));
        var bounce = bounceBand * toward * toward * toward * 0.50f * (1f - 0.7f * s.NoticeMix);
        px = Px.Add(px, (0.45f * liquidR + 0.55f) * bounce, (0.45f * liquidG + 0.50f) * bounce, (0.45f * liquidB + 0.45f) * bounce, bounce * 0.7f);

        // 7. Crisp Meniscus & Contour (Menisco e contorno escuros nítidos para fundos claros)
        var meniscusDark = FastMath.Smooth((u - 0.920f) / 0.045f) * (1f - FastMath.Smooth((u - 0.978f) / 0.015f));
        px = Px.Over(new Px(0f, 0f, 0f, 0.40f * meniscusDark), px);

        var outline = FastMath.Smooth((u - 0.960f) / 0.038f);
        px = Px.Over(new Px(0f, 0f, 0f, 0.48f * outline), px);

        // 8. Key Highlights & Window Glint
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

        if (u > 0.80f && ny < -0.30f)
        {
            var topBand = FastMath.Smooth((u - 0.80f) / 0.08f) * (1f - FastMath.Smooth((u - 0.93f) / 0.05f)) * Math.Clamp((-ny - 0.30f) / 0.5f, 0f, 1f);
            gloss += topBand * 0.30f;
        }

        // 9. BRILHO DE BORDA ORBITAL (guiado por Tone, pulsando nos impactos)
        var orbAngle = s.F.Time * 1.8f + (s.F.Tone - 0.5f) * 3.4f;
        var orbCos = MathF.Cos(orbAngle);
        var orbSin = MathF.Sin(orbAngle);
        var meniscusRing = FastMath.Smooth((u - 0.85f) / 0.065f) * (1f - FastMath.Smooth((u - 0.975f) / 0.015f));
        var arcCos = MathF.Max(0f, zx * orbCos + zy * orbSin);
        var focalArc = arcCos * arcCos * arcCos * arcCos;
        var orbitalPulse = (0.38f + 1.45f * s.F.Impact + 0.45f * s.VoiceEnergy) * (0.85f + 0.35f * s.F.Tone);
        var orbitalGlint = meniscusRing * focalArc * orbitalPulse;
        gloss += orbitalGlint * 1.20f;
        px = Px.Add(px, orbitalGlint * 0.96f, orbitalGlint * 0.98f, orbitalGlint * 1.05f, orbitalGlint * 0.82f);

        // 10. Voice-reactive reflections (listening)
        var react = s.VoiceEnergy * (1f - s.NoticeMix) * (1f - transMix * 0.6f);
        if (react > 0.01f)
        {
            var slosh = FastMath.Sin(sx * (4.5f + 4f * s.Tone) - s.SlosPhase);
            var skyY = -0.50f + 0.07f * slosh * s.VoiceEnergy;
            var sky = (sy - skyY) / (0.075f - 0.03f * s.Tone);
            var skyShape = 1f / (1f + sky * sky) * MathF.Max(0f, 1f - sx * sx * 0.9f) * FastMath.Smooth((u - 0.25f) / 0.4f);
            var bounceY = 0.62f - 0.05f * FastMath.Sin(sx * 5f + s.SlosPhase * 0.8f) * s.VoiceEnergy;
            var floor = (sy - bounceY) / 0.06f;
            var floorShape = 1f / (1f + floor * floor) * MathF.Max(0f, 1f - sx * sx) * FastMath.Smooth((u - 0.30f) / 0.4f);
            var cx = (sx - 0.25f * FastMath.Sin(s.SlosPhase * 0.5f)) / 0.34f;
            var cy = (sy - 0.46f) / 0.13f;
            var caustic = MathF.Max(0f, 1f - (cx * cx + cy * cy));
            caustic *= caustic;
            var flash = 0.55f + 1.1f * s.Impact;
            gloss += react * flash * (0.42f * skyShape + 0.30f * floorShape + 0.50f * caustic);
        }

        gloss = Math.Min(1f, gloss) * s.GlossScale;
        return Px.Add(px, gloss, gloss, gloss, gloss);
    }
}
