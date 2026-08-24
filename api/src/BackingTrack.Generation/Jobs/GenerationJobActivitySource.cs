using System.Diagnostics;

namespace BackingTrack.Generation.Jobs;

public class GenerationJobActivitySource
{
    public const string SourceName = "BackingTrack.GenerationJob";

    public static readonly ActivitySource Instance = new(SourceName);
}