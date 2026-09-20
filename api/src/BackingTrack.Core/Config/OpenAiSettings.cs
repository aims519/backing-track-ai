namespace BackingTrack.Core.Config;

public enum OpenAiApiKind
{
    ChatCompletions,
    Responses,
}

// Mirrors OpenAI's service_tier request field (https://developers.openai.com/api/docs/guides/flex-processing).
// Null/unset on OpenAiSettings means "don't send it", i.e. OpenAI's own "auto" default. Flex only
// applies via the Responses API and is beta with limited model availability, so it's opt-in per step
// rather than a single global switch.
public enum OpenAiServiceTier
{
    Auto,
    Default,
    Flex,
    Scale,
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

    public OpenAiServiceTier? GenerationServiceTier { get; set; }
    public OpenAiServiceTier? RepairServiceTier { get; set; }
    public OpenAiServiceTier? CriticServiceTier { get; set; }
}