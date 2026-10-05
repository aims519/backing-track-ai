namespace BackingTrack.Generation.Jobs;

public static class GenerationModeExtensions
{
    public static string ToMetricTag(this GenerationMode mode)
    {
        return mode switch
        {
            GenerationMode.Precise => "precise",
            GenerationMode.Vibe => "vibe",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }
}