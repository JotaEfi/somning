using System.Diagnostics;
using Whisper.net;

namespace Somning.Engine.Speech;

public sealed class VadGate : IDisposable
{
    private readonly WhisperVadProcessor processor;

    public VadGate(string modelPath, int threads)
    {
        var factory = WhisperVadFactory.FromPath(modelPath);
        try
        {
            processor = factory.CreateBuilder().WithUseGpu(false).WithThreads(threads)
                .WithThreshold(0.5f).WithMinSpeechDuration(TimeSpan.FromMilliseconds(250))
                .WithSpeechPadding(TimeSpan.FromMilliseconds(200)).Build();
        }
        finally { factory.Dispose(); }
        Console.Error.WriteLine("vad_backend=cpu");
    }

    public async Task<(bool HasSpeech, double ElapsedMs)> HasSpeechAsync(Stream audio, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var segments = await processor.DetectSpeechAsync(audio, token);
        watch.Stop();
        return (segments.Count > 0, watch.Elapsed.TotalMilliseconds);
    }

    public void Dispose() => processor.Dispose();
}
