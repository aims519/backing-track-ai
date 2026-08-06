using System.Text.Json.Serialization;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.KnowledgeBase;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ChordEntry))]
[JsonSerializable(typeof(ScaleEntry))]
[JsonSerializable(typeof(StyleEntry))]
internal partial class KnowledgeBaseJsonContext : JsonSerializerContext
{
}