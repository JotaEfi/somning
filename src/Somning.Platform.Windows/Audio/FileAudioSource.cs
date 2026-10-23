using System.Diagnostics;
using NAudio.Wave;
using Somning.Core.Ports;

namespace Somning.Platform.Windows.Audio;

public sealed class FileAudioSource(string wavPath, bool realTime = true, TimeSpan? faultAfter = null) : IAudioSource
{
    private const int SampleRate = 16_000;
    private const int BytesPerSample = sizeof(short);
    private const int FramesPerChunk = SampleRate / 50;
    private readonly string path = Path.GetFullPath(wavPath);
    private readonly TimeSpan? injectedFaultAfter = faultAfter;
    private readonly object lifecycle = new();
    private CancellationTokenSource? stopSource;
    private Task completion = Task.CompletedTask;
    private long framesWritten;
    private int faultRaised;
    private bool started;
    private bool disposed;

    public Task Completion
    {
        get { lock (lifecycle) return completion; }
    }

    public long FramesWritten => Interlocked.Read(ref framesWritten);

    public event Action<float>? Level;
    public event Action<AudioFault>? Faulted;

    public Task<AudioStartInfo> StartAsync(IPcmSink sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ct.ThrowIfCancellationRequested();
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) throw new InvalidOperationException("Audio source can only be started once.");
            started = true;
            stopSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            completion = Task.Run(() => PumpAsync(sink, stopSource.Token), CancellationToken.None);
        }
        return Task.FromResult(new AudioStartInfo(0, "file: 16 kHz mono s16"));
    }

    public async Task StopAsync()
    {
        Task current;
        lock (lifecycle)
        {
            stopSource?.Cancel();
            current = completion;
        }
        await current.ConfigureAwait(false);
    }

    private async Task PumpAsync(IPcmSink sink, CancellationToken ct)
    {
        try
        {
            using var reader = new WaveFileReader(path);
            var sourceIsTargetFormat = reader.WaveFormat.SampleRate == SampleRate &&
                reader.WaveFormat.Channels == 1 && reader.WaveFormat.BitsPerSample == 16 &&
                reader.WaveFormat.Encoding == WaveFormatEncoding.Pcm;
            IWaveProvider pcmProvider = sourceIsTargetFormat ? reader : CaptureConverter.Create(reader);

            var buffer = new byte[FramesPerChunk * BytesPerSample];
            var watch = Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                var bytesRead = pcmProvider.Read(buffer.AsSpan());
                if (bytesRead == 0) break;
                if ((bytesRead & 1) != 0) throw new InvalidDataException("Input ended with a partial PCM16 sample.");
                sink.Write(buffer.AsSpan(0, bytesRead));
                var frames = bytesRead / BytesPerSample;
                var totalFrames = Interlocked.Add(ref framesWritten, frames);
                Level?.Invoke(ComputeRms(buffer.AsSpan(0, bytesRead)));

                if (injectedFaultAfter is { } threshold &&
                    TimeSpan.FromSeconds(totalFrames / (double)SampleRate) >= threshold)
                {
                    RaiseFault(new AudioFault("injected", "Injected audio-source fault."));
                    return;
                }

                if (realTime)
                    await WaitForAudioTimeAsync(watch, totalFrames, ct).ConfigureAwait(false);
            }

            if (realTime)
            {
                Array.Clear(buffer);
                while (!ct.IsCancellationRequested)
                {
                    sink.Write(buffer);
                    Interlocked.Add(ref framesWritten, FramesPerChunk);
                    Level?.Invoke(0);
                    await Task.Delay(TimeSpan.FromMilliseconds(20), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RaiseFault(new AudioFault("callback_error", ex.Message));
        }
    }

    private static async Task WaitForAudioTimeAsync(Stopwatch watch, long frames, CancellationToken ct)
    {
        var target = TimeSpan.FromSeconds(frames / (double)SampleRate);
        var remaining = target - watch.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct).ConfigureAwait(false);
    }

    private void RaiseFault(AudioFault fault)
    {
        if (Interlocked.Exchange(ref faultRaised, 1) == 0)
            Faulted?.Invoke(fault);
    }

    private static float ComputeRms(ReadOnlySpan<byte> pcm)
    {
        double squares = 0;
        var samples = pcm.Length / BytesPerSample;
        for (var index = 0; index < pcm.Length; index += BytesPerSample)
        {
            var sample = (short)(pcm[index] | (pcm[index + 1] << 8));
            var normalized = sample / 32768d;
            squares += normalized * normalized;
        }
        return samples == 0 ? 0 : (float)Math.Sqrt(squares / samples);
    }

    public async ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
        }
        await StopAsync().ConfigureAwait(false);
        stopSource?.Dispose();
    }
}
