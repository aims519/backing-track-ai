using BackingTrack.Core.Config;

namespace BackingTrack.Generation.Steps.TrackGeneration;

public record TrackGenerationProviderInfo(string Provider, string Model, int MaxOutputTokens, OpenAiServiceTier? ServiceTier = null);