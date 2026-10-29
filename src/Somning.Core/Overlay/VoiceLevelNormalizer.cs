namespace Somning.Core.Overlay;

/// <summary>
/// Adaptive gain for the voice level: maps the raw tracker level (RMS, roughly 0.01-0.2 for normal speech) to a
/// usable 0..1 drive. A fixed logarithmic curve gives a perceptual scale; a slowly decaying peak reference then
/// rescales it so quiet and loud microphones both reach the full range. Allocation free.
/// </summary>
public sealed class VoiceLevelNormalizer
{
    public const float FloorDb = -52f;
    public const float CeilingDb = -14f;
    public const float MinimumReference = 0.45f;
    public const float ReferenceDecayPerSecond = 0.07f;

    private float reference = MinimumReference;

    /// <summary>Current peak reference in log-scale units (0..1).</summary>
    public float Reference => reference;

    /// <summary>Log scale in [0, 1] for a raw level; 0 below the floor.</summary>
    public static float ToPerceptual(float rawLevel)
    {
        if (!(rawLevel > 1e-5f)) return 0f;
        var db = 20f * MathF.Log10(Math.Min(rawLevel, 1f));
        return Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);
    }

    /// <summary>Feeds one raw level sample and returns the normalized drive in [0, 1].</summary>
    public float Update(float rawLevel, float deltaSeconds)
    {
        var perceptual = ToPerceptual(rawLevel);
        reference = Math.Max(Math.Max(perceptual, MinimumReference), reference - ReferenceDecayPerSecond * Math.Max(0f, deltaSeconds));
        return Math.Clamp(perceptual / reference, 0f, 1f);
    }

    public void Reset() => reference = MinimumReference;
}
