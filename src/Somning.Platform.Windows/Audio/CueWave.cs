using System.Buffers.Binary;

namespace Somning.Platform.Windows.Audio;

/// <summary>
/// Builds and conditions the in-memory WAV files played for sound cues. A WAV that ends on a non-zero sample, or is cut
/// off by the next cue, is heard as a short click; every cue therefore gets a fade-out and a silent tail so the output
/// device always settles on zero.
/// </summary>
internal static class CueWave
{
    public const int SampleRate = 44100;
    private const int HeaderSize = 44;
    private const int BytesPerSample = 2;
    private const double ToneAmplitude = 0.32;
    private const int FadeInMs = 4;
    private const int FadeOutMs = 28;
    private const int TailSilenceMs = 90;

    /// <summary>A sine tone with raised-cosine attack and release, followed by silence.</summary>
    public static byte[] Tone(int frequencyHz, int durationMs)
    {
        var count = SampleRate * durationMs / 1000;
        var samples = new short[count];
        for (var i = 0; i < count; i++)
        {
            var value = Math.Sin(2 * Math.PI * frequencyHz * i / SampleRate) * ToneAmplitude;
            samples[i] = (short)Math.Round(value * short.MaxValue);
        }
        return Build(Condition(samples));
    }

    /// <summary>
    /// Fades the start and end of a canonical 16-bit mono PCM WAV and appends a silent tail. Input that is not in that
    /// format is returned untouched rather than risking a corrupt file.
    /// </summary>
    public static byte[] Smooth(byte[] wav)
    {
        if (!TryReadSamples(wav, out var samples)) return wav;
        return Build(Condition(samples));
    }

    private static short[] Condition(short[] samples)
    {
        var result = new short[samples.Length + SampleRate * TailSilenceMs / 1000];
        Array.Copy(samples, result, samples.Length);
        Fade(result, 0, Math.Min(samples.Length, SampleRate * FadeInMs / 1000), fadeIn: true);
        var fadeOut = Math.Min(samples.Length, SampleRate * FadeOutMs / 1000);
        Fade(result, samples.Length - fadeOut, fadeOut, fadeIn: false);
        return result;
    }

    private static void Fade(short[] samples, int start, int length, bool fadeIn)
    {
        for (var i = 0; i < length; i++)
        {
            var position = length == 1 ? 1.0 : (double)i / (length - 1);
            var gain = 0.5 - 0.5 * Math.Cos(Math.PI * (fadeIn ? position : 1 - position));
            samples[start + i] = (short)Math.Round(samples[start + i] * gain);
        }
    }

    private static bool TryReadSamples(byte[] wav, out short[] samples)
    {
        samples = [];
        if (wav.Length <= HeaderSize || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8)) return false;
        var span = wav.AsSpan();
        var format = BinaryPrimitives.ReadUInt16LittleEndian(span[20..]);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(span[22..]);
        var rate = BinaryPrimitives.ReadInt32LittleEndian(span[24..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(span[34..]);
        if (format != 1 || channels != 1 || rate != SampleRate || bits != 16) return false;
        if (!span.Slice(36, 4).SequenceEqual("data"u8)) return false;
        var dataLength = Math.Min(BinaryPrimitives.ReadInt32LittleEndian(span[40..]), wav.Length - HeaderSize);
        samples = new short[dataLength / BytesPerSample];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(span[(HeaderSize + i * BytesPerSample)..]);
        return true;
    }

    private static byte[] Build(short[] samples)
    {
        var dataLength = samples.Length * BytesPerSample;
        var wav = new byte[HeaderSize + dataLength];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataLength);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * BytesPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], BytesPerSample);
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataLength);
        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(span[(HeaderSize + i * BytesPerSample)..], samples[i]);
        return wav;
    }
}
