namespace BackingTrack.Core.Config;

public class OpenAiSettings
{
    public required string BaseUrl { get; set; }
    public required string Model { get; set; }
    public required string ApiKey { get; set; }
    public required int MaxOutputTokens { get; set; }
}