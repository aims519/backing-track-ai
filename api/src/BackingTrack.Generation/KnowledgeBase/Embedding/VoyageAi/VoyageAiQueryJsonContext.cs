using System.Text.Json.Serialization;

namespace BackingTrack.Generation.KnowledgeBase.Embedding.VoyageAi;

[JsonSerializable(typeof(VoyageAiQueryEmbeddingRequestDto))]
[JsonSerializable(typeof(VoyageAiQueryEmbeddingResponseDto))]
public partial class VoyageAiQueryJsonContext : JsonSerializerContext { }