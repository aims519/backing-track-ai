using System.Text.Json.Serialization;

namespace BackingTrack.Generation.KnowledgeBase.Embedding.VoyageAi;

public class VoyageAiQueryEmbeddingResponseDto
{
    [JsonPropertyName("data")]
    public VoyageAiQueryEmbeddingDataItemDto[] Data { get; init; } = [];
}

public class VoyageAiQueryEmbeddingDataItemDto
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; init; } = [];
}