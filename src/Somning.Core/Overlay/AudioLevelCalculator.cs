namespace Somning.Core.Overlay;

/// <summary>
/// Pure 16-bit PCM measurements for the overlay waveform. Inputs are little-endian signed samples and outputs are
/// normalized to [0.0f, 1.0f] so callers never need to know the sample width (AUD-03).
/// </summary>
public static class AudioLevelCalculator
{
    private const double FullScale = 32768d;

    public static float ComputeRms(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty) return 0f;

        double sumSquares = 0;
        foreach (var sample in samples)
        {
            var normalized = sample / FullScale;
            sumSquares += normalized * normalized;
        }
        return Clamp01((float)Math.Sqrt(sumSquares / samples.Length));
    }

    public static float ComputeRms(ReadOnlySpan<byte> pcm)
    {
        var sampleCount = pcm.Length / sizeof(short);
        if (sampleCount == 0) return 0f;

        double sumSquares = 0;
        for (var index = 0; index < sampleCount; index++)
        {
            var normalized = ReadSample(pcm, index * sizeof(short)) / FullScale;
            sumSquares += normalized * normalized;
        }
        return Clamp01((float)Math.Sqrt(sumSquares / sampleCount));
    }

    public static float ComputePeak(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty) return 0f;

        var peak = 0;
        foreach (var sample in samples)
        {
            var magnitude = Math.Abs((int)sample);
            if (magnitude > peak) peak = magnitude;
        }
        return Clamp01((float)(peak / FullScale));
    }

    public static float ComputePeak(ReadOnlySpan<byte> pcm)
    {
        var sampleCount = pcm.Length / sizeof(short);
        if (sampleCount == 0) return 0f;

        var peak = 0;
        for (var index = 0; index < sampleCount; index++)
        {
            var magnitude = Math.Abs((int)ReadSample(pcm, index * sizeof(short)));
            if (magnitude > peak) peak = magnitude;
        }
        return Clamp01((float)(peak / FullScale));
    }

    /// <summary>Reads one little-endian PCM16 sample; a trailing odd byte is ignored by the callers.</summary>
    private static short ReadSample(ReadOnlySpan<byte> pcm, int offset) =>
        (short)(pcm[offset] | (pcm[offset + 1] << 8));

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
}
