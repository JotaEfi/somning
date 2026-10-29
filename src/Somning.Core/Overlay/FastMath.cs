namespace Somning.Core.Overlay;

/// <summary>Cheap math for per-pixel shading. Accuracy is visual (about 1e-3), not numerical.</summary>
public static class FastMath
{
    private const float TwoPi = MathF.PI * 2f;
    private const float InvTwoPi = 1f / TwoPi;

    /// <summary>Sine via a parabolic approximation with one refinement pass; any finite angle, max error about 0.001.</summary>
    public static float Sin(float angle)
    {
        var turns = angle * InvTwoPi;
        turns -= MathF.Floor(turns + 0.5f);          // -0.5..0.5
        var x = turns * TwoPi;                       // -pi..pi
        var y = 1.27323954f * x - 0.405284735f * x * MathF.Abs(x);
        return 0.225f * (y * MathF.Abs(y) - y) + y;
    }

    /// <summary>Square root through the hardware reciprocal-square-root estimate (about 12 bits); x must be finite and non-negative.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static float Sqrt(float x) => x * MathF.ReciprocalSqrtEstimate(x + 1e-12f);

    public static float Cos(float angle) => Sin(angle + MathF.PI * 0.5f);

    public static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
