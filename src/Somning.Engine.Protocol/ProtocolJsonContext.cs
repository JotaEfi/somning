using System.Text.Json.Serialization;
namespace Somning.Engine.Protocol;
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(EngineMessage))]
public partial class ProtocolJsonContext : JsonSerializerContext;
