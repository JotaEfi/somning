using System.Diagnostics;
using System.Text;
using Somning.Core.Engine;
using Whisper.net;
namespace Somning.Engine;
public sealed class WhisperHost : IDisposable
{
    private readonly WhisperFactory factory;
    private readonly WhisperProcessor processor;
    public double LoadMs { get; }
    public double WarmMs { get; private set; }
    public WhisperHost(EngineOptions options)
    {
        var watch = Stopwatch.StartNew();
        factory = WhisperFactory.FromPath(options.Model, new WhisperFactoryOptions
        { UseGpu = options.Backend == "vulkan", UseFlashAttention = options.FlashAttention });
        try
        {
            processor = factory.CreateBuilder().WithLanguage(EngineDefaults.Language).WithNoContext()
                .WithThreads(options.Threads).WithPrompt(EngineDefaults.InitialPrompt).Build();
        }
        catch { factory.Dispose(); throw; }
        LoadMs = watch.Elapsed.TotalMilliseconds;
    }
    public async Task WarmAsync()
    {
        var watch = Stopwatch.StartNew();
        await foreach (var _ in processor.ProcessAsync(new float[16000])) { }
        WarmMs = watch.Elapsed.TotalMilliseconds;
    }
    public async Task<(string Text, float NoSpeech)> TranscribeAsync(Stream audio, CancellationToken token)
    {
        var text = new StringBuilder(); float noSpeech = 0;
        await foreach (var segment in processor.ProcessAsync(audio, token))
        { text.Append(segment.Text); noSpeech = Math.Max(noSpeech, segment.NoSpeechProbability); }
        return (text.ToString().Trim(), noSpeech);
    }
    public void Dispose() { processor.Dispose(); factory.Dispose(); }
}
