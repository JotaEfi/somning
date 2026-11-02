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

    /// <summary>
    /// Zero-crossing rate mapped to 0..1 as a cheap voice-brightness proxy: voiced low vowels cross rarely (near 0),
    /// fricatives and high pitch cross often (near 1). Silence and near-silence return 0.
    /// </summary>
    public static float ComputeBrightness(ReadOnlySpan<byte> pcm)
    {
        var sampleCount = pcm.Length / sizeof(short);
        if (sampleCount < 2) return 0f;

        const int Deadband = 96;   // ignore sign flips caused by microphone noise around zero
        var crossings = 0;
        var previous = ReadSample(pcm, 0);
        for (var index = 1; index < sampleCount; index++)
        {
            var current = ReadSample(pcm, index * sizeof(short));
            if ((previous > Deadband && current < -Deadband) || (previous < -Deadband && current > Deadband)) crossings++;
            if (Math.Abs((int)current) > Deadband) previous = current;
        }

        // Speech spans roughly 0.02 (low vowels) to 0.35 (sibilants) crossings per sample at 16 kHz.
        var rate = crossings / (float)(sampleCount - 1);
        return Clamp01((rate - 0.02f) / 0.28f);
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
