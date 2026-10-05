namespace BackingTrack.Generation.Tests.Steps.Critic;

// MeterListener subscribes by meter name, so every test class that creates a CriticMetrics (directly or
// through CriticAiClient) shares this collection to keep their measurements from leaking into each other.
[CollectionDefinition(Name)]
public class CriticMetricsCollection
{
    public const string Name = "CriticMetrics";
}