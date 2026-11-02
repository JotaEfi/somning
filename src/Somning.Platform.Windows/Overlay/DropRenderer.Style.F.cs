using Somning.Core.Overlay;

namespace Somning.Platform.Windows.Overlay;

public sealed partial class DropRenderer
{
    private Px ShadeGlassF(in Scene s, float dx, float dy, float dyl, float nx, float ny, float u)
    {
        var u2 = u * u;
        var nz = FastMath.Sqrt(MathF.Max(0f, 1f - u2));
        var rim1 = 1f - nz;
        var mix = s.BackdropMix;

        // Sphere-space coordinates normalized to the drop's bounding box (ribbons stretch along the capsule).
        var sx = dx * s.InvBoxW;
        var sy = dyl * s.InvBoxH;

        // One optical displacement bends the desktop, window, sky/floor and graphite ribbons together.
        var wave = 0f;
        var crest = 0f;
        FAddWave(s.F.Wave0Front, s.F.Wave0Amp, u, ref wave, ref crest);
        FAddWave(s.F.Wave1Front, s.F.Wave1Amp, u, ref wave, ref crest);
        wave *= 1f - s.NoticeMix;
        crest *= 1f - s.NoticeMix;
        var slide = s.F.Slosh * (1f - s.NoticeMix);
        var rxOpt = sx + 0.18f * slide + 0.32f * wave * nx;
        var ryOpt = sy + 0.26f * wave * ny + 0.035f * slide * sx;
        var waveReach = wave * s.RefractReach * 0.65f;

        // Glass body: dark, mostly transparent in the middle, denser toward the rim, lit from the upper left.
        // Over a captured backdrop the body gets clearer so the refracted desktop shows through.
        var lit = MathF.Max(0f, -0.45f * nx - 0.60f * ny + 0.66f * nz);
        var bodyA = (0.18f + 0.33f * rim1 * rim1) * (1f - 0.36f * mix) + 0.58f * s.NoticeMix;
        var bodyGray = 0.045f + 0.14f * lit;
        var body = new Px(bodyGray * bodyA, bodyGray * bodyA, bodyGray * bodyA, bodyA);

        // Refraction: a lens shows the desktop compressed toward the center and, near the rim, what lies around it.
        float refR = 0f, refG = 0f, refB = 0f;
        var px = default(Px);
        if (mix > 0f)
        {
            // A folded radial mapping magnifies the center and reverses the local image near the rim.
            // Sample actual backdrop detail, with identical channel coordinates (no invented color fringes).
            var reach = s.RefractReach * (0.90f * u2 - 1.90f * u2 * u2);
            FEnvironment(in s, s.CenterX + dx * 0.76f + nx * (reach + waveReach) + slide * s.RefractReach * 0.10f, s.CenterY + dy * 0.76f + ny * (reach + waveReach), out refR, out refG, out refB);
            var lensA = mix * (0.60f + 0.30f * u2);
            px = new Px(refR * lensA, refG * lensA, refB * lensA, lensA);
        }

        px = Px.Over(body, px);

        var depth = 1f - FastMath.Smooth((u - 0.80f) / 0.2f) * 0.55f;
        var strength = 0f;
        var intensity = 0f;
        float liquidR = 0.14f, liquidG = 0.14f, liquidB = 0.14f;
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

            var la = Math.Min(0.28f, strength * 0.38f) * (1f - s.NeutralMix);
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
        var glow = (bloom + scatter) * 0.40f;
        px = Px.Add(px, liquidR * glow, liquidG * glow, liquidB * glow, glow * 0.5f);

        // The central graphite inclusion is part of this glass in BOTH listening and transcribing.
        // Voice widens its dense core; slosh and incoming fronts shear all three contours together.
        var column = Math.Clamp((int)(s.CenterX + dx), 0, waveCenter.Length - 1);
        var spread = 0.112f + 0.115f * s.VoiceEnergy + 0.035f * s.TranscribeMix;
        var bandY = sy + 0.16f * wave * (0.5f + MathF.Abs(sx)) + 0.055f * slide * sx;
        var bandD0 = (bandY - waveCenter[column]) / spread;
        var bandD1 = (bandY - waveUpper[column]) / (spread * 0.62f);
        var bandD2 = (bandY - waveLower[column]) / (spread * 0.55f);
        var core = 1f / (1f + bandD0 * bandD0);
        var band = 0.73f * core + 0.23f / (1f + bandD1 * bandD1) + 0.20f / (1f + bandD2 * bandD2);
        var graphiteA = Math.Min(0.93f, band * (0.91f + 0.18f * s.VoiceEnergy)) * depth * (1f - 0.65f * s.NoticeMix);
        var metalGradient = Math.Clamp((bandY - waveCenter[column]) / spread * 0.5f + 0.5f, 0f, 1f);
        var graphite = 0.010f + 0.043f * metalGradient * metalGradient + 0.018f * (1f - core);
        px = Px.Over(new Px(graphite * graphiteA, (graphite + 0.008f * s.TranscribeMix) * graphiteA,
            (graphite + 0.018f * s.TranscribeMix) * graphiteA, graphiteA), px);
        var seam = (bandY - waveCenter[column] + spread * 0.54f) / (0.012f + 0.004f * s.Tone);
        var seam2 = (bandY - waveUpper[column] + spread * 0.23f) / 0.009f;
        // A dim, wide reflected vein flows beneath two razor-thin silver seams.
        var vein = (bandY - waveCenter[column] - 0.065f - 0.025f * wave) / 0.035f;
        var silver = 0.055f * core / (1f + vein * vein) + (0.24f + 0.12f * s.Impact) / (1f + seam * seam) + 0.095f / (1f + seam2 * seam2);
        silver *= depth * (1f - 0.75f * s.NoticeMix);
        px = Px.Add(px, silver * (1f - 0.14f * s.TranscribeMix), silver,
            silver * (1f + 0.12f * s.TranscribeMix), silver);

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
            FEnvironment(in s, s.CenterX + dx * 1.1f + ox * d0 * 0.94f, s.CenterY + dy * 1.1f + oy * d0 * 0.94f, out var aR, out var aG, out _);
            FEnvironment(in s, s.CenterX + dx * 1.1f + ox * d0 * (1.08f - 0.14f * s.NeutralMix), s.CenterY + dy * 1.1f + oy * d0 * (1.08f - 0.14f * s.NeutralMix), out _, out var bG, out var bB);
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
        var ex1 = rxOpt + 0.36f * s.HighlightSpread;
        var ey1 = ryOpt + 0.50f;
        var along1 = (ex1 * 0.82f - ey1 * 0.58f) / (0.42f * s.HighlightSpread);
        var across = (ex1 * 0.58f + ey1 * 0.82f) / 0.075f;
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

        // Voice-reactive reflections (listening only): a sky band sloshing along the top, a bounce band along the
        // bottom and a caustic focus inside the lower rim. Phoneme onsets flash them; brighter tones tighten them.
        var react = (0.12f + 0.78f * s.VoiceEnergy + 0.12f * s.TranscribeMix) * (1f - s.NoticeMix);
        if (react > 0.01f)
        {
            var slosh = FastMath.Sin(rxOpt * (4.5f + 4f * s.Tone) - s.SlosPhase);
            var skyY = -0.50f + 0.07f * slosh * s.VoiceEnergy;
            var sky = (ryOpt - skyY) / (0.075f - 0.03f * s.Tone);
            var skyShape = 1f / (1f + sky * sky) * MathF.Max(0f, 1f - sx * sx * 0.9f) * FastMath.Smooth((u - 0.25f) / 0.4f);
            var bounceY = 0.62f - 0.05f * FastMath.Sin(rxOpt * 5f + s.SlosPhase * 0.8f) * s.VoiceEnergy;
            var floor = (ryOpt - bounceY) / 0.06f;
            var floorShape = 1f / (1f + floor * floor) * MathF.Max(0f, 1f - sx * sx) * FastMath.Smooth((u - 0.30f) / 0.4f);
            var cx = (rxOpt - 0.12f * slide) / (0.36f - 0.18f * s.Tone);
            var cy = (ryOpt - 0.50f) / (0.13f - 0.04f * s.Tone);
            var caustic = MathF.Max(0f, 1f - (cx * cx + cy * cy));
            caustic *= caustic * (1f + 1.4f * s.Impact);
            var flash = 0.55f + 1.1f * s.Impact;
            gloss += react * flash * (0.42f * skyShape + 0.30f * floorShape + 0.65f * caustic);
        }

        // Crests pick up the light without washing out the central graphite.
        gloss += crest * (0.15f + 0.14f * lit) * FastMath.Smooth((u - 0.35f) / 0.45f);
        gloss = Math.Min(1f, gloss) * s.GlossScale;
        return Px.Add(px, gloss, gloss, gloss, gloss);
    }

    // Compact polynomial wave: positive/negative slopes, bright crest, no trigonometry or allocations.
    private static void FAddWave(float front, float amplitude, float radius, ref float slope, ref float crest)
    {
        if (amplitude <= 0.003f) return;
        var q = (radius - (1f - front)) * 4f;
        var envelope = 1f - q * q;
        if (envelope <= 0f) return;
        var gain = amplitude * (1f - front * front);
        var e2 = envelope * envelope;
        slope += gain * e2 * q;
        crest += gain * e2 * envelope;
    }

    // Preserve desktop color; Classic's Env intentionally desaturates listening to graphite.
    private static void FEnvironment(in Scene s, float x, float y, out float r, out float g, out float b)
    {
        s.Tex!.SampleTexel(s.BackdropX0 + x * s.BackdropSx, s.BackdropY0 + y * s.BackdropSy, out r, out g, out b);
        var gray = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        r += (gray - r) * 0.08f;
        g += (gray - g) * 0.08f;
        b += (gray - b) * 0.08f;
    }
}
