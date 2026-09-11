using System.Text.Json.Serialization;

using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.KnowledgeBase.Embedding;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ChordEntry))]
[JsonSerializable(typeof(ScaleEntry))]
[JsonSerializable(typeof(StyleEntry))]
[JsonSerializable(typeof(MatchedStyle))]
[JsonSerializable(typeof(IReadOnlyList<MatchedChord>))]
[JsonSerializable(typeof(IReadOnlyList<MatchedScale>))]
public partial class KnowledgeBaseJsonContext : JsonSerializerContext
{
}