namespace Somning.Core.Overlay;

/// <summary>
/// Time-based exponential smoothing for the waveform bars: fast attack, slow release, independent of frame rate.
/// Allocation-free after construction, so it is safe to tick at 60 fps.
/// </summary>
public sealed class BarSmoother
{
    public const float DefaultAttackSeconds = 0.045f;
    public const float DefaultReleaseSeconds = 0.22f;

    private readonly float[] values;
    private readonly float attackSeconds;
    private readonly float releaseSeconds;

    public BarSmoother(int count, float attackSeconds = DefaultAttackSeconds, float releaseSeconds = DefaultReleaseSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfNegative(attackSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(releaseSeconds);
        values = new float[count];
        this.attackSeconds = attackSeconds;
        this.releaseSeconds = releaseSeconds;
    }

    public int Count => values.Length;

    public ReadOnlySpan<float> Values => values;

    public void Reset(float value) => Array.Fill(values, Math.Clamp(value, 0f, 1f));

    /// <summary>Moves each bar toward its target; targets beyond <see cref="Count"/> are ignored, missing ones read as 0.</summary>
    public void Update(ReadOnlySpan<float> targets, float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return;
        for (var i = 0; i < values.Length; i++)
        {
            var target = Math.Clamp(i < targets.Length ? targets[i] : 0f, 0f, 1f);
            var tau = target > values[i] ? attackSeconds : releaseSeconds;
            values[i] += (target - values[i]) * Coefficient(tau, deltaSeconds);
        }
    }

    internal static float Coefficient(float tau, float deltaSeconds) =>
        tau <= 0f ? 1f : 1f - MathF.Exp(-deltaSeconds / tau);
}
