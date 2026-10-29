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
    public const float MaxElongation = 0.5f;
    public const float MaxLean = 0.12f;
    private const float AttackSeconds = 0.06f;
    private const float ReleaseSeconds = 0.30f;
    private const float FastAttackSeconds = 0.022f;
    private const float FastReleaseSeconds = 0.14f;
    private const float TwoPi = MathF.PI * 2f;

    private Spring elongation = new(0f);
    private Spring lean = new(0f);
    private float body;
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
        body = 0f;
        fast = 0f;
        clock = 0f;
        ripplePhase = 0f;
        Ripple = 0f;
    }

    /// <param name="drive">Normalized, unsmoothed voice drive 0..1 (0 when not listening).</param>
    /// <param name="idle">Slow idle pulse 0..1 used while transcribing (a calm elongated breathing).</param>
    /// <param name="dt">Seconds since the previous update.</param>
    public void Update(float drive, float idle, float dt)
    {
        if (dt <= 0f) return;
        clock += dt;
        drive = Math.Clamp(drive, 0f, 1f);

        body += (drive - body) * (1f - MathF.Exp(-dt / (drive > body ? AttackSeconds : ReleaseSeconds)));
        fast += (drive - fast) * (1f - MathF.Exp(-dt / (drive > fast ? FastAttackSeconds : FastReleaseSeconds)));
        var transient = Math.Max(0f, fast - body);

        var breath = 0.014f * (1f - body) * MathF.Sin(TwoPi * clock / 2.7f);
        var shaped = MathF.Pow(body, 0.85f);
        var target = MaxElongation * (0.82f * shaped + 0.6f * transient) + breath + 0.07f * idle;
        target = Math.Clamp(target, -0.02f, MaxElongation);

        elongation.Step(target, 15f, 0.5f, dt);
        elongation.Value = Math.Clamp(elongation.Value, -0.04f, MaxElongation * 1.12f);

        // Trailing end: the fat side leans against the direction of motion; a slow drift keeps it alive.
        var drift = 0.05f * body * MathF.Sin(clock * 0.8f);
        var leanTarget = Math.Clamp(-elongation.Velocity * 0.03f + drift, -MaxLean, MaxLean);
        lean.Step(leanTarget, 9f, 0.7f, dt);
        lean.Value = Math.Clamp(lean.Value, -MaxLean, MaxLean);

        Ripple = Math.Clamp(transient * 3.2f + 0.3f * body, 0f, 1f);
        ripplePhase += dt * (2.0f + 6f * Ripple);
        if (ripplePhase > TwoPi * 8f) ripplePhase -= TwoPi * 8f;
    }
}
