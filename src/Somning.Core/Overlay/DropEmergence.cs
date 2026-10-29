namespace Somning.Core.Overlay;

/// <summary>Shape of the viscous entrance at one instant (all lengths in units of the resting drop radius).</summary>
/// <param name="Active">False once the entrance is over (the drop is the normal floating drop).</param>
/// <param name="Rise">Drop center position between the screen edge (0) and its resting place (1); may overshoot.</param>
/// <param name="Size">Drop size multiplier (mass flows from the edge into the drop).</param>
/// <param name="Squash">Vertical elongation, positive = taller and narrower (volume preserving).</param>
/// <param name="Goo">1 while the edge pool / filament exist, fading to 0 when they are gone.</param>
/// <param name="Neck">Filament radius at the pinch point; 0 once it has snapped.</param>
/// <param name="TopTail">Fraction of the upper filament half still hanging under the drop (1 = reaches the pinch point).</param>
/// <param name="BottomTail">Fraction of the lower filament half still standing on the edge pool.</param>
/// <param name="Pool">Radius of the viscous mound bulging out of the screen edge.</param>
/// <param name="VoiceGate">0..1 weight of the voice deformation (released once the drop has settled).</param>
public readonly record struct EmergeShape(
    bool Active,
    float Rise,
    float Size,
    float Squash,
    float Goo,
    float Neck,
    float TopTail,
    float BottomTail,
    float Pool,
    float VoiceGate)
{
    public static readonly EmergeShape Rest = new(false, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 1f);
}

/// <summary>
/// Deterministic timeline of the "pulled goo" entrance: a mound swells out of the bottom screen edge, the drop is
/// dragged upward on a thinning liquid neck, the neck pinches off (surface tension), both tails retract, and the
/// drop settles with a damped elastic wobble. Pure function of time, no allocation.
/// </summary>
public static class DropEmergence
{
    public const float BreakSeconds = 0.32f;
    public const float DurationSeconds = 0.64f;
    /// <summary>Where the filament snaps, as a fraction of the drop-to-edge span (0 = drop, 1 = edge).</summary>
    public const float PinchAt = 0.56f;
    public const float StartRise = -0.12f;
    public const float BreakRise = 0.90f;
    public const float MaxNeck = 0.46f;

    private const float SwellSeconds = 0.09f;
    private const float WobbleOmega = MathF.PI * 2f / 0.19f;
    private const float WobbleDecay = 9.5f;
    private const float SquashAtBreak = 0.16f;
    private const float TopTailTau = 0.035f;
    private const float BottomTailTau = 0.055f;
    private const float PoolSinkSeconds = 0.16f;

    public static EmergeShape Evaluate(float seconds)
    {
        if (seconds >= DurationSeconds || float.IsNaN(seconds)) return EmergeShape.Rest;
        var t = Math.Max(0f, seconds);

        if (t < BreakSeconds)
        {
            var x = t / BreakSeconds;
            // Viscous pull: slow to tear away from the edge, fast through the middle, resisting as the neck thins.
            var pull = Smooth(Math.Clamp((t - 0.03f) / (BreakSeconds - 0.03f), 0f, 1f));
            var rise = StartRise + (BreakRise - StartRise) * pull;
            var size = 0.32f + 0.62f * Smooth(Math.Clamp(t / (BreakSeconds * 0.9f), 0f, 1f));
            var swell = Smooth(Math.Clamp(t / SwellSeconds, 0f, 1f));
            // The mound gives its mass to the drop as it rises.
            var pool = swell * (1.05f - 0.45f * pull);
            // Surface tension: the neck thins slowly, then collapses just before the snap.
            var neck = MaxNeck * MathF.Pow(1f - x, 1.4f) * swell;
            var squash = SquashAtBreak * x * x;
            return new EmergeShape(true, rise, size, squash, 1f, neck, 1f, 1f, pool, 0f);
        }

        var tau = t - BreakSeconds;
        var envelope = MathF.Exp(-WobbleDecay * tau);
        var wobble = envelope * MathF.Cos(WobbleOmega * tau);
        // Released: the drop snaps up past its rest spot and settles; the stretched drop recoils into a wobble.
        var settleRise = 1f - (1f - BreakRise) * wobble;
        var settleSize = 1f - 0.06f * MathF.Exp(-14f * tau);
        var settleSquash = SquashAtBreak * wobble;
        var top = MathF.Exp(-tau / TopTailTau);
        var bottom = MathF.Exp(-tau / BottomTailTau);
        var sink = Math.Clamp(1f - tau / PoolSinkSeconds, 0f, 1f);
        var poolNow = 0.60f * sink * sink;
        var goo = Math.Clamp(Math.Max(Math.Max(top, bottom), poolNow / 0.60f) * 1.2f, 0f, 1f);
        if (goo < 0.02f) goo = 0f;
        var gate = Smooth(Math.Clamp((tau - 0.08f) / 0.2f, 0f, 1f));
        return new EmergeShape(true, settleRise, settleSize, settleSquash, goo, 0f, top, bottom, poolNow, gate);
    }

    private static float Smooth(float x) => x * x * (3f - 2f * x);
}
