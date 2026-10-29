namespace Somning.Core.Overlay;

/// <summary>
/// Organic outline of the orb as a normalized polar radius table rho(theta). A few low harmonics drift with the flow
/// phase and grow with the voice level, so the drop wobbles like a living liquid. Allocation-free after construction.
/// </summary>
public sealed class OrbShape
{
    public const int Samples = 128;
    public const float IdleAmplitude = 0.010f;
    public const float VoiceAmplitude = 0.075f;
    public const float VoiceGrowth = 0.12f;
    public const float BreathGrowth = 0.022f;

    private static readonly int[] Harmonics = [2, 3, 4, 5, 7];
    private static readonly float[] Weights = [1.00f, 0.80f, 0.45f, 0.25f, 0.10f];
    private static readonly float[] Speeds = [0.90f, -1.30f, 1.70f, -0.80f, 2.30f];

    private readonly float[] radii = new float[Samples + 1]; // last entry duplicates the first for lerp wrap

    public ReadOnlySpan<float> Radii => radii;
    public float MinRadius { get; private set; } = 1f;
    public float MaxRadius { get; private set; } = 1f;

    /// <summary>Normalized radius at <paramref name="theta"/> (radians, any range) by linear interpolation.</summary>
    public float RadiusAt(float theta)
    {
        var u = (theta + MathF.PI) * (Samples / (2f * MathF.PI));
        return Lerp(u);
    }

    internal float Lerp(float u)
    {
        var i = (int)u;
        if ((uint)i >= Samples) i = Math.Clamp(i, 0, Samples - 1);
        var f = u - i;
        return radii[i] + (radii[i + 1] - radii[i]) * f;
    }

    public void Update(in OrbFrame frame)
    {
        var calm = frame.Mode == OverlayMode.Transcribing ? 0.55f : 1f;
        var amp = (IdleAmplitude + VoiceAmplitude * frame.Level) * calm;
        var growth = 1f + VoiceGrowth * frame.Level + BreathGrowth * frame.Breath;
        var step = 2f * MathF.PI / Samples;

        var min = float.MaxValue;
        var max = float.MinValue;
        for (var i = 0; i <= Samples; i++)
        {
            var theta = -MathF.PI + i * step;
            var wobble = 0f;
            for (var h = 0; h < Harmonics.Length; h++)
            {
                var phase = Harmonics[h] * theta + frame.Flow * Speeds[h] + h * 1.3f;
                wobble += Weights[h] * OrbMath.Sin(phase);
            }

            var r = growth * (1f + amp * wobble);
            radii[i] = r;
            if (r < min) min = r;
            if (r > max) max = r;
        }

        radii[Samples] = radii[0];
        MinRadius = min;
        MaxRadius = max;
    }
}
