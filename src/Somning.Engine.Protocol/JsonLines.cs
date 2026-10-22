using System.Text.Json;
namespace Somning.Engine.Protocol;
public static class JsonLines
{
    public const int MaxLineLength = 65536;
    public static string Serialize(EngineMessage message) =>
        JsonSerializer.Serialize(message, ProtocolJsonContext.Default.EngineMessage);
    public static bool TryDeserialize(string? line, out EngineMessage? message, out string? error)
    {
        message = null; error = ErrorCodes.BadRequest;
        if (line is null || line.Length > MaxLineLength) return false;
        try
        {
            message = JsonSerializer.Deserialize(line, ProtocolJsonContext.Default.EngineMessage);
            if (message is null) return false;
            error = null; return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return false; }
    }
}
