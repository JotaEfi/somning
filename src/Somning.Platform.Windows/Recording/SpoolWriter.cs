using Somning.Core.Ports;
using NAudio.Wave;

namespace Somning.Platform.Windows.Recording;

public sealed class SpoolWriter : IPcmSink, IDisposable
{
    private const int FlushIntervalBytes = 32_000;
    private readonly FileStream stream;
    private readonly WaveFileWriter writer;
    private long bytesSinceFlush;
    private long sampleCount;
    private double sumSquares;
    private int disposed;

    public SpoolWriter(string wavPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(wavPath));
        if (directory is not null) Directory.CreateDirectory(directory);

        stream = new FileStream(wavPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        try
        {
            writer = new WaveFileWriter(stream, new WaveFormat(16_000, 16, 1));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public TimeSpan Duration => TimeSpan.FromSeconds(writer.Length / (double)writer.WaveFormat.AverageBytesPerSecond);

    public float Rms => sampleCount == 0 ? 0 : (float)Math.Sqrt(sumSquares / sampleCount);

    public void Write(ReadOnlySpan<byte> pcm16kMonoS16)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if ((pcm16kMonoS16.Length & 1) != 0)
            throw new ArgumentException("PCM16 input must contain complete samples.", nameof(pcm16kMonoS16));

        writer.Write(pcm16kMonoS16);
        for (var i = 0; i < pcm16kMonoS16.Length; i += sizeof(short))
        {
            var sample = (short)(pcm16kMonoS16[i] | (pcm16kMonoS16[i + 1] << 8));
            var normalized = sample / 32768d;
            sumSquares += normalized * normalized;
            sampleCount++;
        }

        bytesSinceFlush += pcm16kMonoS16.Length;
        if (bytesSinceFlush >= FlushIntervalBytes)
        {
            writer.Flush();
            bytesSinceFlush = 0;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            writer.Dispose();
        }
        finally
        {
            stream.Dispose();
        }
    }
}
