namespace BackingTrack.Core.Config;

public class AnthropicSettings
{
    public required string Model { get; set; }
    public required string ApiKey { get; set; }
    public required int MaxOutputTokens { get; set; }
}