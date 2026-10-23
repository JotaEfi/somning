using System.Diagnostics;
using System.Text;
using Somning.Core.Engine;
using Whisper.net;
using Somning.Engine.Protocol;
using Somning.Engine.Speech;
namespace Somning.Engine;
public sealed class WhisperHost : IDisposable
{
    private readonly WhisperFactory factory;
    private readonly WhisperProcessor processor;
    public SpeechGate SpeechGate { get; }
    public double LoadMs { get; }
    public double WarmMs { get; private set; }
    public WhisperHost(EngineOptions options)
    {
        var watch = Stopwatch.StartNew();
        factory = WhisperFactory.FromPath(options.Model, new WhisperFactoryOptions
        { UseGpu = options.Backend == "vulkan", UseFlashAttention = options.FlashAttention });
        try
        {
            SpeechGate = new SpeechGate(new VadGate(options.Vad, options.Threads));
            processor = factory.CreateBuilder().WithLanguage(EngineDefaults.Language).WithNoContext()
                .WithThreads(options.Threads).WithPrompt(EngineDefaults.InitialPrompt).Build();
        }
        catch { SpeechGate?.Dispose(); factory.Dispose(); throw; }
        LoadMs = watch.Elapsed.TotalMilliseconds;
    }
    public async Task WarmAsync()
    {
        var watch = Stopwatch.StartNew();
        await foreach (var _ in processor.ProcessAsync(new float[16000])) { }
        WarmMs = watch.Elapsed.TotalMilliseconds;
    }
    public async Task<(string Text, float NoSpeech, IReadOnlyList<SegmentMessage> Segments)> TranscribeAsync(
        Stream audio, CancellationToken token)
    {
        var text = new StringBuilder(); float noSpeech = 0;
        var segments = new List<SegmentMessage>();
        await foreach (var segment in processor.ProcessAsync(audio, token))
        {
            text.Append(segment.Text); noSpeech = Math.Max(noSpeech, segment.NoSpeechProbability);
            segments.Add(new SegmentMessage(segment.Text, segment.Start.TotalMilliseconds,
                segment.End.TotalMilliseconds, segment.NoSpeechProbability));
        }
        return (text.ToString().Trim(), noSpeech, segments);
    }
    public void Dispose()
    {
        try { processor.Dispose(); } catch { }
        SpeechGate.Dispose();
        factory.Dispose();
    }
}
