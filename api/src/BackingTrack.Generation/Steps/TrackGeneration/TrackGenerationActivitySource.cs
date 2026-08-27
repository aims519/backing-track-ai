using System.Diagnostics;

namespace BackingTrack.Generation.Steps.TrackGeneration;

public static class TrackGenerationActivitySource
{
    public const string SourceName = "BackingTrack.TrackGeneration";

    public static readonly ActivitySource Instance = new(SourceName);
}