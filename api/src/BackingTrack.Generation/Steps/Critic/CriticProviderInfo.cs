using BackingTrack.Core.Config;

namespace BackingTrack.Generation.Steps.Critic;

public record CriticProviderInfo(string Provider, string Model, int MaxOutputTokens, OpenAiServiceTier? ServiceTier = null);