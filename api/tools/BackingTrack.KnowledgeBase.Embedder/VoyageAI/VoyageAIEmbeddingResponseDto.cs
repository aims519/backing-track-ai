using System.Text.Json.Serialization;

namespace BackingTrack.KnowledgeBase.Embedder.VoyageAI;

public class VoyageAIEmbeddingResponseDto
{
    [JsonPropertyName("data")]
    public EmbeddingDataItemDto[] Data { get; init; } = Array.Empty<EmbeddingDataItemDto>();
}

public class EmbeddingDataItemDto
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; init; } = [];
}