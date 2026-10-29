namespace Somning.Core.Overlay;

/// <summary>Small allocation-free approximations used by the per-pixel orb shader (errors well below 1/255).</summary>
public static class OrbMath
{
    private const float TwoPi = 6.2831853f;

    /// <summary>Sine of an angle in radians; max error about 0.001.</summary>
    public static float Sin(float x)
    {
        x -= MathF.Floor(x * (1f / TwoPi) + 0.5f) * TwoPi; // [-pi, pi]
        var y = 1.27323954f * x - 0.40528473f * x * MathF.Abs(x);
        return 0.225f * (y * MathF.Abs(y) - y) + y;
    }

    /// <summary>Angle of (x, y) in radians in [-pi, pi]; max error about 1e-4.</summary>
    public static float Atan2(float y, float x)
    {
        var ax = MathF.Abs(x);
        var ay = MathF.Abs(y);
        var mx = MathF.Max(ax, ay);
        if (mx <= 1e-12f) return 0f;
        var a = MathF.Min(ax, ay) / mx;
        var s = a * a;
        var r = ((-0.0464964749f * s + 0.15931422f) * s - 0.327622764f) * s * a + a;
        if (ay > ax) r = 1.57079637f - r;
        if (x < 0f) r = 3.14159274f - r;
        return y < 0f ? -r : r;
    }

    public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
