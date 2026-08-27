namespace BackingTrack.Core.Config;

public class OpenAiSettings
{
    public required string BaseUrl { get; set; }
    public required string GenerationModel { get; set; }
    public required string RepairModel { get; set; }
    public required string ApiKey { get; set; }
    public required int GenerationMaxOutputTokens { get; set; }
    public required int RepairMaxOutputTokens { get; set; }
}