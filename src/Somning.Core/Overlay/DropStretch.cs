namespace Somning.Core.Overlay;

/// <summary>
/// Voice-driven elongation of the glass drop. The drop stretches horizontally into a pill (and leans like a
/// teardrop while it moves) in proportion to a smoothed voice level with an asymmetric envelope (fast attack, slow
/// release); onsets (phonemes) add a quick transient on top, and silence relaxes it back to a breathing circle.
/// Elongation <c>e</c> maps to width x(1+e), height and depth x1/sqrt(1+e), so the 3D volume stays constant
/// (squash and stretch). Pure, allocation free; not thread-safe.
/// </summary>
public sealed class DropStretch
{
    public const float MaxElongation = 0.88f;
    public const float MaxLean = 0.22f;
    public const float MinWaveIntervalSeconds = 0.075f;
    public const float WaveTravelSeconds = 0.30f;
    private const float WaveDecaySeconds = 0.24f;
    private const float OnsetThreshold = 0.05f;
    private const float OnsetRearm = 0.02f;
    private const float AttackSeconds = 0.035f;
    private const float ReleaseSeconds = 0.19f;
    private const float FastAttackSeconds = 0.012f;
    private const float FastReleaseSeconds = 0.075f;
    private const float TwoPi = MathF.PI * 2f;

    private Spring elongation = new(0f);
    private Spring lean = new(0f);
    private Spring slosh = new(0f);
    private float wave0Front, wave0Amp, wave1Front, wave1Amp;
    private float lastOnset = -1f;
    private bool isArmed = true;
    private float idleClock;
    private float body;
    private float tone;
    private float fast;
    private float clock;
    private float ripplePhase;

    /// <summary>Horizontal elongation, 0 = circle, up to about <see cref="MaxElongation"/> (small overshoot allowed).</summary>
    public float Elongation => elongation.Value;

    /// <summary>Asymmetric lean (teardrop) in -<see cref="MaxLean"/>..<see cref="MaxLean"/>; positive = fat end on the right.</summary>
    public float Lean => lean.Value;

    /// <summary>Surface ripple energy 0..1 (phoneme transients plus a little of the body level).</summary>
    public float Ripple { get; private set; }

    public float RipplePhase => ripplePhase;

    /// <summary>Sideways slide of the body, -1 (low tones, left) .. 1 (high tones, right); springy, overshoots.</summary>
    public float Slosh => slosh.Value;

    /// <summary>Wave 0 travel, 0 = at the rim, 1 = at the center; only meaningful while <see cref="Wave0Amp"/> is above zero.</summary>
    public float Wave0Front => wave0Front;
    public float Wave0Amp => wave0Amp;
    public float Wave1Front => wave1Front;
    public float Wave1Amp => wave1Amp;

    /// <summary>Smoothed voice brightness 0..1 (low vowels near 0, sibilants and high pitch near 1).</summary>
    public float Tone => tone;

    /// <summary>Sharp 0..1 flare on each phoneme onset; drives the reflection flashes on the glass.</summary>
    public float Impact { get; private set; }

    /// <summary>Width and height/depth factors for an elongation; width x height x depth is exactly 1.</summary>
    public static (float Width, float Height) Scales(float elongation)
    {
        var e = Math.Max(-0.2f, elongation);
        return (1f + e, 1f / MathF.Sqrt(1f + e));
    }

    public void Reset()
    {
        elongation.Reset(0f);
        lean.Reset(0f);
        slosh.Reset(0f);
        wave0Front = wave0Amp = wave1Front = wave1Amp = 0f;
        lastOnset = -1f;
        isArmed = true;
        idleClock = 0f;
        body = 0f;
        tone = 0f;
        Impact = 0f;
        fast = 0f;
        clock = 0f;
        ripplePhase = 0f;
        Ripple = 0f;
    }

    /// <param name="drive">Normalized, unsmoothed voice drive 0..1 (0 when not listening).</param>
    /// <param name="idle">Slow idle pulse 0..1 used while transcribing (a calm elongated breathing).</param>
    /// <param name="dt">Seconds since the previous update.</param>
    /// <param name="toneInput">Raw voice brightness 0..1 (zero-crossing based); ignored while silent.</param>
    public void Update(float drive, float idle, float dt, float toneInput = 0f)
    {
        if (dt <= 0f) return;
        clock += dt;
        drive = Math.Clamp(drive, 0f, 1f);

        body += (drive - body) * (1f - MathF.Exp(-dt / (drive > body ? AttackSeconds : ReleaseSeconds)));
        fast += (drive - fast) * (1f - MathF.Exp(-dt / (drive > fast ? FastAttackSeconds : FastReleaseSeconds)));
        var transient = Math.Max(0f, fast - body);

        var breath = 0.014f * (1f - body) * MathF.Sin(TwoPi * clock / 2.7f);
        var shaped = MathF.Pow(body, 0.85f);
        var toneTarget = drive > 0.05f ? Math.Clamp(toneInput, 0f, 1f) : 0f;
        tone += (toneTarget - tone) * (1f - MathF.Exp(-dt / 0.07f));
        var target = MaxElongation * (0.95f * shaped + 2.2f * transient) + breath + 0.07f * idle;
        target = Math.Clamp(target, -0.02f, MaxElongation);

        elongation.Step(target, 38f, 0.26f, dt);   // under-damped: the squash overshoots and rings
        elongation.Value = Math.Clamp(elongation.Value, -0.04f, MaxElongation * 1.12f);

        // Tone slides the body sideways (low left, high right) and leans it; onsets kick the slide.
        var side = Math.Clamp((tone - 0.4f) * 2.6f, -1f, 1f) * MathF.Sqrt(body);
        slosh.Velocity += (tone - 0.4f) * transient * 9f;
        slosh.Step(side, 15f, 0.36f, dt);
        slosh.Value = Math.Clamp(slosh.Value, -1.3f, 1.3f);

        // Trailing end: the fat side leans against the direction of motion; a slow drift keeps it alive.
        var drift = (0.08f * MathF.Sin(clock * 0.8f) + 0.12f * (tone - 0.5f)) * body;
        var leanTarget = Math.Clamp(-elongation.Velocity * 0.05f + drift + 0.11f * slosh.Value, -MaxLean, MaxLean);
        lean.Step(leanTarget, 12f, 0.5f, dt);
        lean.Value = Math.Clamp(lean.Value, -MaxLean, MaxLean);

        StepWaves(drive, transient, dt);

        Ripple = Math.Clamp(transient * 6.5f + 0.45f * body, 0f, 1f);
        Impact = Math.Clamp(transient * 8f + 0.6f * Math.Max(wave0Amp * (1f - wave0Front), wave1Amp * (1f - wave1Front)), 0f, 1f);
        ripplePhase += dt * (2.0f + 10f * Ripple + 7f * tone * body);
        if (ripplePhase > TwoPi * 8f) ripplePhase -= TwoPi * 8f;
    }

    // Each sound onset launches a wave from the rim toward the center; two can travel at once, at least
    // MinWaveIntervalSeconds apart. In silence a faint, slow swell keeps the glass alive.
    private void StepWaves(float drive, float transient, float dt)
    {
        AdvanceWave(ref wave0Front, ref wave0Amp, dt);
        AdvanceWave(ref wave1Front, ref wave1Amp, dt);

        if (transient < OnsetRearm) isArmed = true;
        if (isArmed && transient > OnsetThreshold && clock - lastOnset >= MinWaveIntervalSeconds)
        {
            isArmed = false;
            Launch(Math.Clamp(0.40f + 3.2f * transient, 0f, 1f));
        }
        else if (drive < 0.02f && body < 0.03f)
        {
            idleClock += dt;
            if (idleClock >= 2.4f && wave0Amp <= 0f && wave1Amp <= 0f)
            {
                idleClock = 0f;
                Launch(0.16f);
            }
        }
    }

    private static void AdvanceWave(ref float front, ref float amp, float dt)
    {
        if (amp <= 0f) return;
        front += dt / WaveTravelSeconds;
        amp *= MathF.Exp(-dt / WaveDecaySeconds);
        if (front >= 1f || amp < 0.01f) amp = 0f;
    }

    private void Launch(float amplitude)
    {
        lastOnset = clock;
        // Reuse a free slot, otherwise recycle the wave that has traveled farthest.
        if (wave0Amp <= 0f || (wave1Amp > 0f && wave0Front >= wave1Front))
        {
            wave0Front = 0f;
            wave0Amp = amplitude;
        }
        else
        {
            wave1Front = 0f;
            wave1Amp = amplitude;
        }
    }
}
