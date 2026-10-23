using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Somning.Platform.Windows.Audio;
using Xunit;

namespace Somning.Integration.Tests.Audio;

public sealed class CaptureConverterTests
{
    [Fact]
    public void Converts_48khz_stereo_float_to_16khz_mono_pcm16_preserving_frequency()
    {
        const int inputRate = 48_000;
        var input = new MemoryWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(inputRate, 2),
            MakeFloatPcm(inputRate, 2, frame => MathF.Sin(2 * MathF.PI * 1_000 * frame / inputRate)));

        var converter = CaptureConverter.Create(input);
        var output = ReadAll(converter);

        Assert.Equal(16_000, converter.WaveFormat.SampleRate);
        Assert.Equal(1, converter.WaveFormat.Channels);
        Assert.Equal(16, converter.WaveFormat.BitsPerSample);
        Assert.InRange(output.Length, 32_000 - 320, 32_000 + 320);
        Assert.InRange(EstimateFrequency(output, converter.WaveFormat.SampleRate), 980, 1_020);
    }

    [Fact]
    public void Converts_44100_hz_mono_pcm16_and_preserves_duration()
    {
        const int inputRate = 44_100;
        var input = new MemoryWaveProvider(new WaveFormat(inputRate, 16, 1),
            MakePcm16(inputRate, frame => MathF.Sin(2 * MathF.PI * 440 * frame / inputRate)));

        var converter = CaptureConverter.Create(input);
        var output = ReadAll(converter);
        var duration = output.Length / (double)converter.WaveFormat.AverageBytesPerSecond;

        Assert.Equal(16_000, converter.WaveFormat.SampleRate);
        Assert.Equal(1, converter.WaveFormat.Channels);
        Assert.Equal(16, converter.WaveFormat.BitsPerSample);
        Assert.InRange(duration, .990, 1.010);
    }

    [Fact]
    public void Downmixes_six_float_channels_by_averaging()
    {
        const int inputRate = 48_000;
        var input = new MemoryWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(inputRate, 6),
            MakeFloatPcm(inputRate, 6, _ => .5f));

        var converter = CaptureConverter.Create(input);
        var output = ReadAll(converter);
        var peak = ReadOnlySpanSamples(output).Max(sample => Math.Abs((int)sample));

        Assert.Equal(16_000, converter.WaveFormat.SampleRate);
        Assert.Equal(1, converter.WaveFormat.Channels);
        Assert.Equal(16, converter.WaveFormat.BitsPerSample);
        Assert.InRange(peak, (int)(.475 * short.MaxValue), (int)(.525 * short.MaxValue));
    }

    [Fact]
    public void Silent_capture_packet_is_added_as_zero_samples()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        var buffer = new BufferedWaveProvider(format) { ReadFully = false };
        var nonzero = MakeFloatPcm(480, 2, _ => .75f);

        CaptureConverter.AppendPacket(buffer, nonzero, AudioClientBufferFlags.Silent);
        var output = new byte[nonzero.Length];
        var read = buffer.Read(output.AsSpan());

        Assert.Equal(nonzero.Length, read);
        Assert.True(output.All(value => value == 0), "Silent device packets must be written as zero samples.");
    }

    private static byte[] ReadAll(IWaveProvider provider)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8_192];
        int read;
        while ((read = provider.Read(buffer.AsSpan())) > 0)
            output.Write(buffer, 0, read);
        return output.ToArray();
    }

    private static byte[] MakeFloatPcm(int sampleRate, int channels, Func<int, float> sampleForFrame)
    {
        var data = new byte[sampleRate * channels * sizeof(float)];
        for (var frame = 0; frame < sampleRate; frame++)
        for (var channel = 0; channel < channels; channel++)
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((frame * channels + channel) * sizeof(float)), sampleForFrame(frame));
        return data;
    }

    private static byte[] MakePcm16(int sampleRate, Func<int, float> sampleForFrame)
    {
        var data = new byte[sampleRate * sizeof(short)];
        for (var frame = 0; frame < sampleRate; frame++)
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(frame * sizeof(short)), (short)(sampleForFrame(frame) * short.MaxValue));
        return data;
    }

    private static short[] ReadOnlySpanSamples(byte[] pcm)
    {
        var samples = new short[pcm.Length / sizeof(short)];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * sizeof(short)));
        return samples;
    }

    private static double EstimateFrequency(byte[] pcm, int sampleRate)
    {
        var samples = ReadOnlySpanSamples(pcm);
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
            if ((samples[i - 1] < 0 && samples[i] >= 0) || (samples[i - 1] >= 0 && samples[i] < 0))
                crossings++;
        return crossings / (2d * samples.Length / sampleRate);
    }

    private sealed class MemoryWaveProvider(WaveFormat waveFormat, byte[] data) : IWaveProvider
    {
        private int offset;
        public WaveFormat WaveFormat { get; } = waveFormat;

        public int Read(Span<byte> buffer)
        {
            var bytes = Math.Min(buffer.Length, data.Length - offset);
            data.AsSpan(offset, bytes).CopyTo(buffer);
            this.offset += bytes;
            return bytes;
        }
    }
}
