using System.Text.Json.Serialization;
namespace Somning.Engine.Protocol;
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ReadyMessage), "ready")]
[JsonDerivedType(typeof(TranscribeRequest), "transcribe")]
[JsonDerivedType(typeof(CancelRequest), "cancel")]
[JsonDerivedType(typeof(ShutdownRequest), "shutdown")]
[JsonDerivedType(typeof(ResultMessage), "result")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
public abstract record EngineMessage;
public sealed record ReadyMessage(string Backend, string Model, double LoadMs, double WarmMs,
    bool GpuInUse, bool FlashAttention) : EngineMessage;
public sealed record TranscribeRequest(string Id, string WavPath, string Language = "pt",
    string? Prompt = null) : EngineMessage;
public sealed record CancelRequest(string Id) : EngineMessage;
public sealed record ShutdownRequest : EngineMessage;
public sealed record ResultMessage(string Id, string Text, string Outcome, string? Reason,
    double DurationMs, double VadMs, double InferMs, float NoSpeech, string Backend, string Model) : EngineMessage;
public sealed record ErrorMessage(string? Id, string Code, string Message) : EngineMessage;
