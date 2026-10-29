namespace Somning.Core.Overlay;

/// <summary>Pure periodic shapes used for the idle "breathing" bars, the transcribing shimmer and the pulse.</summary>
public static class OverlayWaves
{
    public const float IdleBase = AudioLevelTracker.MinimumBarHeight;
    public const float IdleAmplitude = 0.12f;
    public const float IdlePeriodSeconds = 1.8f;
    public const float ShimmerPeriodSeconds = 1.1f;
    public const float ShimmerMin = 0.22f;
    public const float ShimmerMax = 0.85f;

    /// <summary>Quiet per-bar ripple in [IdleBase, IdleBase + IdleAmplitude]; keeps the pill alive in silence.</summary>
    public static float Idle(int index, float seconds)
    {
        var phase = seconds / IdlePeriodSeconds - index * 0.13f;
        return IdleBase + IdleAmplitude * Unit(phase);
    }

    /// <summary>Travelling wave across the bars in [ShimmerMin, ShimmerMax] for the transcribing state.</summary>
    public static float Shimmer(int index, int count, float seconds)
    {
        var phase = seconds / ShimmerPeriodSeconds - (count <= 1 ? 0f : index / (float)count);
        return ShimmerMin + (ShimmerMax - ShimmerMin) * Unit(phase);
    }

    /// <summary>Sine pulse in [0, 1] with the given period.</summary>
    public static float Pulse(float seconds, float periodSeconds) =>
        periodSeconds <= 0f ? 0f : Unit(seconds / periodSeconds);

    // 0..1 sine of a phase measured in cycles.
    private static float Unit(float cycles) => 0.5f + 0.5f * MathF.Sin(2f * MathF.PI * cycles);
}
