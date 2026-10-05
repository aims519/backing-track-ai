using System.Text.Json.Serialization;

namespace BackingTrack.Generation.Notifications;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ApnsPayload))]
[JsonSerializable(typeof(ApnsJwtHeader))]
[JsonSerializable(typeof(ApnsJwtClaims))]
[JsonSerializable(typeof(ApnsRejection))]
internal partial class ApnsJsonContext : JsonSerializerContext
{
}