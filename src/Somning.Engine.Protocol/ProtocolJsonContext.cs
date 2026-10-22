using System.Text.Json.Serialization;
namespace Somning.Engine.Protocol;
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(EngineMessage))]
public partial class ProtocolJsonContext : JsonSerializerContext;
