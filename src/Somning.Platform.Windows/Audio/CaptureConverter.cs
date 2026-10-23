using System.Buffers;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Somning.Platform.Windows.Audio;

public static class CaptureConverter
{
    public static IWaveProvider Create(IWaveProvider source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.WaveFormat.Channels <= 0)
            throw new ArgumentException("Audio input must contain at least one channel.", nameof(source));

        var samples = source.ToSampleProvider();
        ISampleProvider mono = samples.WaveFormat.Channels == 1
            ? samples
            : new AveragingMonoSampleProvider(samples);
        return new WdlResamplingSampleProvider(mono, 16_000).ToWaveProvider16();
    }

    internal static void AppendPacket(BufferedWaveProvider destination, ReadOnlySpan<byte> packet, AudioClientBufferFlags flags)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!flags.HasFlag(AudioClientBufferFlags.Silent))
        {
            destination.AddSamples(packet);
            return;
        }

        if (packet.IsEmpty) return;
        var silence = ArrayPool<byte>.Shared.Rent(packet.Length);
        try
        {
            var zeroed = silence.AsSpan(0, packet.Length);
            zeroed.Clear();
            destination.AddSamples(zeroed);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(silence);
        }
    }

    private sealed class AveragingMonoSampleProvider : ISampleProvider
    {
        private const int FramesPerRead = 2_048;
        private readonly ISampleProvider source;
        private readonly int channels;
        private readonly float[] input;

        public AveragingMonoSampleProvider(ISampleProvider source)
        {
            this.source = source;
            channels = source.WaveFormat.Channels;
            input = new float[FramesPerRead * channels];
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<float> buffer)
        {
            var writtenFrames = 0;
            while (writtenFrames < buffer.Length)
            {
                var framesRequested = Math.Min(buffer.Length - writtenFrames, input.Length / channels);
                var samplesRead = source.Read(input.AsSpan(0, framesRequested * channels));
                if (samplesRead == 0) break;
                if (samplesRead % channels != 0)
                    throw new InvalidDataException("Audio input ended in the middle of a channel frame.");

                var framesRead = samplesRead / channels;
                for (var frame = 0; frame < framesRead; frame++)
                {
                    var sum = 0f;
                    var firstSample = frame * channels;
                    for (var channel = 0; channel < channels; channel++)
                        sum += input[firstSample + channel];
                    buffer[writtenFrames + frame] = sum / channels;
                }

                writtenFrames += framesRead;
                if (framesRead < framesRequested) break;
            }

            return writtenFrames;
        }
    }
}
