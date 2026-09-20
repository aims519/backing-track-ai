namespace BackingTrack.Core.Config;

public enum OpenAiApiKind
{
    ChatCompletions,
    Responses,
}

public class OpenAiSettings
{
    public required string BaseUrl { get; set; }
    public required string GenerationModel { get; set; }
    public required string RepairModel { get; set; }
    public required string CriticModel { get; set; }
    public required string ApiKey { get; set; }
    public required int GenerationMaxOutputTokens { get; set; }
    public required int RepairMaxOutputTokens { get; set; }
    public required int CriticMaxOutputTokens { get; set; }

    // Newer model families (e.g. gpt-5.6-terra) are Responses-API-only; older ones stay on
    // Chat Completions. Defaulting to ChatCompletions keeps existing config working unchanged.
    public OpenAiApiKind GenerationApi { get; set; } = OpenAiApiKind.ChatCompletions;
    public OpenAiApiKind RepairApi { get; set; } = OpenAiApiKind.ChatCompletions;
    public OpenAiApiKind CriticApi { get; set; } = OpenAiApiKind.ChatCompletions;
}