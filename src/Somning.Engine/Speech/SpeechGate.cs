using Somning.Core.Engine;
using Somning.Core.Text;

namespace Somning.Engine.Speech;

public sealed record GateOutcome(bool Accepted, string? Reason, double VadMs);

public sealed class SpeechGate(VadGate vad) : IDisposable
{
    public async Task<GateOutcome> BeforeTranscriptionAsync(Stream audio, double durationMs, CancellationToken token)
    {
        if (durationMs < EngineDefaults.MinDurationSeconds * 1000)
            return new(false, "too_short", 0);
        audio.Position = 0;
        var (hasSpeech, elapsed) = await vad.HasSpeechAsync(audio, token);
        audio.Position = 0;
        return hasSpeech ? new(true, null, elapsed) : new(false, "vad_no_speech", elapsed);
    }

    public static PostFilterResult AfterTranscription(string text, float noSpeech) =>
        PostFilter.Evaluate(text, noSpeech, EngineDefaults.NoSpeechThreshold);

    public void Dispose() => vad.Dispose();
}
