using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Somning.Platform.Windows.Audio;

/// <summary>
/// One render stream kept open for the life of the app and fed continuously (silence between cues). Opening and
/// closing the output device for every cue makes some drivers, especially USB headsets, pop when the device powers
/// down; a stream that never stops has no such edge. Cues are mixed into it.
/// </summary>
internal sealed class CueOutput : IDisposable
{
    private const int HeaderSize = 44;
    private const int LatencyMs = 60;
    private readonly object gate = new();
    private WasapiPlayer? output;
    private MixingSampleProvider? mixer;

    /// <summary>Mixes a conditioned 16-bit mono WAV into the open stream, opening it on first use.</summary>
    public void Play(Stream wav)
    {
        using var copy = new MemoryStream();
        wav.CopyTo(copy);
        var bytes = copy.ToArray();
        if (bytes.Length <= HeaderSize) return;
        var cue = ToStereo(bytes);
        lock (gate)
        {
            try
            {
                EnsureOpen();
                mixer!.AddMixerInput(new ArraySampleProvider(cue, mixer.WaveFormat));
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                CloseLocked(); // Device vanished or changed: reopen on the next cue.
                throw new InvalidOperationException("Cue output unavailable.", ex);
            }
        }
    }

    private void EnsureOpen()
    {
        if (output is not null) return;
        mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(CueWave.SampleRate, 2)) { ReadFully = true };
        var device = new WasapiPlayerBuilder().WithSharedMode().WithLatency(LatencyMs).Build();
        device.PlaybackStopped += (_, _) => { lock (gate) { if (ReferenceEquals(output, device)) CloseLocked(); } };
        device.Init(new SampleToWaveProvider(mixer));
        device.Play();
        output = device;
    }

    private void CloseLocked()
    {
        var device = output;
        output = null;
        mixer = null;
        try { device?.Dispose(); } catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
    }

    private static float[] ToStereo(byte[] wav)
    {
        var frames = (wav.Length - HeaderSize) / 2;
        var stereo = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var sample = BitConverter.ToInt16(wav, HeaderSize + i * 2) / 32768f;
            stereo[i * 2] = sample;
            stereo[i * 2 + 1] = sample;
        }
        return stereo;
    }

    public void Dispose()
    {
        lock (gate) CloseLocked();
    }

    private sealed class ArraySampleProvider(float[] samples, WaveFormat format) : ISampleProvider
    {
        private int position;
        public WaveFormat WaveFormat => format;

        public int Read(Span<float> buffer)
        {
            var n = Math.Min(buffer.Length, samples.Length - position);
            samples.AsSpan(position, n).CopyTo(buffer);
            position += n;
            return n;
        }
    }
}
