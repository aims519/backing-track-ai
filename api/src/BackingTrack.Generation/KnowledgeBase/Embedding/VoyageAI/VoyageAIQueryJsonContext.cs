using System.Text.Json.Serialization;

namespace BackingTrack.Generation.KnowledgeBase.Embedding.VoyageAI;

[JsonSerializable(typeof(VoyageAIQueryEmbeddingRequestDto))]
[JsonSerializable(typeof(VoyageAIQueryEmbeddingResponseDto))]
public partial class VoyageAIQueryJsonContext : JsonSerializerContext { }