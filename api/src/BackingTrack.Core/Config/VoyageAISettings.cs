namespace BackingTrack.Core.Config;

public class VoyageAISettings
{
    public required string ApiKey { get; set; }
    public required string RestApiBaseUrl { get; set; }
    public required string EmbeddingModel { get; set; }
}