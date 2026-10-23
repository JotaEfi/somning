using System.Text.Json;
using System.Text.Json.Serialization;

namespace Somning.Core.Recording;

[JsonConverter(typeof(RecordingStageJsonConverter))]
public enum RecordingStage
{
    Captured,
    Transcribed,
    ClipboardSet,
    Pasted,
    PasteDeferred,
    Empty,
    Failed,
    Recovered,
    Cancelled
}

public sealed class RecordingStageJsonConverter : JsonConverter<RecordingStage>
{
    public override RecordingStage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Recording stage must be a string.");

        return reader.GetString() switch
        {
            "captured" => RecordingStage.Captured,
            "transcribed" => RecordingStage.Transcribed,
            "clipboard_set" => RecordingStage.ClipboardSet,
            "pasted" => RecordingStage.Pasted,
            "paste_deferred" => RecordingStage.PasteDeferred,
            "empty" => RecordingStage.Empty,
            "failed" => RecordingStage.Failed,
            "recovered" => RecordingStage.Recovered,
            "cancelled" => RecordingStage.Cancelled,
            _ => throw new JsonException("Unknown recording stage.")
        };
    }

    public override void Write(Utf8JsonWriter writer, RecordingStage value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            RecordingStage.Captured => "captured",
            RecordingStage.Transcribed => "transcribed",
            RecordingStage.ClipboardSet => "clipboard_set",
            RecordingStage.Pasted => "pasted",
            RecordingStage.PasteDeferred => "paste_deferred",
            RecordingStage.Empty => "empty",
            RecordingStage.Failed => "failed",
            RecordingStage.Recovered => "recovered",
            RecordingStage.Cancelled => "cancelled",
            _ => throw new JsonException("Unknown recording stage.")
        });
    }
}
