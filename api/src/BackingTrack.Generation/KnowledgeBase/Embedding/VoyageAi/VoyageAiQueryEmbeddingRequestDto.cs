using System.Text.Json.Serialization;

namespace BackingTrack.Generation.KnowledgeBase.Embedding.VoyageAi;

public class VoyageAiQueryEmbeddingRequestDto
{
    [JsonPropertyName("input")]
    public required string[] Input { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input_type")]
    public required string InputType { get; init; }
}