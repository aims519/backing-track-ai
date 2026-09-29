using System.ComponentModel.DataAnnotations;

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
    [Required, Url]
    public required string BaseUrl { get; set; }
    [Required]
    public required string GenerationModel { get; set; }
    [Required]
    public required string RepairModel { get; set; }
    [Required]
    public required string CriticModel { get; set; }
    [Required]
    public required string ApiKey { get; set; }
    [Range(1, int.MaxValue)]
    public required int GenerationMaxOutputTokens { get; set; }
    [Range(1, int.MaxValue)]
    public required int RepairMaxOutputTokens { get; set; }
    [Range(1, int.MaxValue)]
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