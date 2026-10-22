namespace Somning.Core.Engine;
public sealed record EngineTranscription(string Text, string Outcome, string? Reason, string? ErrorCode,
    bool Retryable, long DurationMs, long VadMs, long InferMs, long QueueMs, long LoadMs, long WarmMs,
    string Backend, string Model, float NoSpeech);
