using System.Diagnostics;

namespace BackingTrack.Generation.Steps.TrackPersistence;

public static class TrackPersistenceActivitySource
{
    public const string SourceName = "BackingTrack.TrackPersistence";

    public static readonly ActivitySource Instance = new(SourceName);
}