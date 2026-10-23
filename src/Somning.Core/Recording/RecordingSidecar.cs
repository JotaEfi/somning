using System.Text.Json.Serialization;

namespace Somning.Core.Recording;

public sealed record RecordingSidecar(
    string Id,
    DateTimeOffset CreatedUtc,
    RecordingStage Stage,
    string Mode,
    string? Text,
    string? Outcome,
    string? Reason,
    int Attempts,
    string? LastError)
{
    public RecordingSidecar WithStage(RecordingStage stage) => this with { Stage = stage };
    public RecordingSidecar WithText(string? text) => this with { Text = text };
    public RecordingSidecar WithOutcome(string? outcome) => this with { Outcome = outcome };
    public RecordingSidecar WithReason(string? reason) => this with { Reason = reason };
    public RecordingSidecar WithAttempts(int attempts) => this with { Attempts = attempts };
    public RecordingSidecar WithLastError(string? lastError) => this with { LastError = lastError };
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Default,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(RecordingSidecar))]
[JsonSerializable(typeof(RecordingSidecar[]))]
public partial class RecordingSidecarJsonContext : JsonSerializerContext;
