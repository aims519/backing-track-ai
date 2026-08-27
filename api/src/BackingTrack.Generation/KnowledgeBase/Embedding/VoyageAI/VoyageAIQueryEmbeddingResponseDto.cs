using System.Text.Json.Serialization;

namespace BackingTrack.Generation.KnowledgeBase.Embedding.VoyageAI;

public class VoyageAIQueryEmbeddingResponseDto
{
    [JsonPropertyName("data")]
    public VoyageAIQueryEmbeddingDataItemDto[] Data { get; init; } = [];
}

public class VoyageAIQueryEmbeddingDataItemDto
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; init; } = [];
}