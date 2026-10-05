using System.Diagnostics;

namespace BackingTrack.Generation.Steps.Critic;

public static class CriticActivitySource
{
    public const string SourceName = "BackingTrack.Critic";

    public static readonly ActivitySource Instance = new(SourceName);
}