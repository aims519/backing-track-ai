using BackingTrack.Core.Config;

namespace BackingTrack.Generation.Steps.TrackRepair;

public record TrackRepairProviderInfo(string Provider, string Model, int MaxOutputTokens, OpenAiServiceTier? ServiceTier = null);