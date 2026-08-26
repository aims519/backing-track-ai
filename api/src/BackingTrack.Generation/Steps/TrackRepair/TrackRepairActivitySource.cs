using System.Diagnostics;

namespace BackingTrack.Generation.Steps.TrackRepair;

public static class TrackRepairActivitySource
{
    public const string SourceName = "BackingTrack.TrackRepair";

    public static readonly ActivitySource Instance = new(SourceName);
}