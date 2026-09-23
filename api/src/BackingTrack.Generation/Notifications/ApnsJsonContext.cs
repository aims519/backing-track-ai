using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Notifications;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ApnsPayload))]
internal partial class ApnsJsonContext : JsonSerializerContext
{
}