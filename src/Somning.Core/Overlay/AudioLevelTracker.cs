namespace Somning.Core.Overlay;

/// <summary>
/// Frame-based level smoother for the overlay: rises quickly on speech and falls slowly on silence. The waveform
/// bars are deterministic for a given (count, level) pair, with a 0.15 baseline and a center-weighted harmonic shape.
/// </summary>
public sealed class AudioLevelTracker
{
    public const float DefaultAttack = 0.6f;
    public const float DefaultDecay = 0.12f;
    public const float MinimumBarHeight = 0.15f;

    private float level;

    public AudioLevelTracker(float attack = DefaultAttack, float decay = DefaultDecay)
    {
        if (attack is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(attack), attack, "Attack must be in [0, 1].");
        if (decay is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(decay), decay, "Decay must be in [0, 1].");
        Attack = attack;
        Decay = decay;
    }

    public float Attack { get; }
    public float Decay { get; }

    /// <summary>Smoothed level; safe to read from the UI thread while the audio callback writes it.</summary>
    public float Level => Volatile.Read(ref level);
    public float CurrentLevel => Level;

    /// <summary>Feeds one normalized target (RMS or peak) and returns the smoothed level.</summary>
    public float Update(float target)
    {
        target = Math.Clamp(target, 0f, 1f);
        var current = Level;
        var coefficient = target > current ? Attack : Decay;
        var next = Math.Clamp(current + (target - current) * coefficient, 0f, 1f);
        Volatile.Write(ref level, next);
        return next;
    }

    public float Update(ReadOnlySpan<short> samples) => Update(AudioLevelCalculator.ComputeRms(samples));

    public float Update(ReadOnlySpan<byte> pcm) => Update(AudioLevelCalculator.ComputeRms(pcm));

    public void Reset() => Volatile.Write(ref level, 0f);

    /// <summary>Builds <paramref name="count"/> bars in [0.15f, 1.0f]; higher energy grows them from the center.</summary>
    public float[] GenerateWaveformBars(int count, float level)
    {
        if (count <= 0) return [];

        var energy = Math.Clamp(level, 0f, 1f);
        var bars = new float[count];
        var center = (count - 1) / 2d;
        for (var index = 0; index < count; index++)
        {
            var distance = center == 0d ? 0d : Math.Abs(index - center) / center;
            var envelope = 1d - 0.45d * distance;
            var harmonic = 0.8d + 0.2d * Math.Sin((index + 1) * Math.PI / (count + 1));
            var height = MinimumBarHeight + (1d - MinimumBarHeight) * energy * envelope * harmonic;
            bars[index] = (float)Math.Clamp(height, MinimumBarHeight, 1d);
        }
        return bars;
    }

    public float[] GenerateWaveformBars(int count) => GenerateWaveformBars(count, Level);
}
