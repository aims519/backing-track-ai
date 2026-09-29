using System.ComponentModel.DataAnnotations;

namespace BackingTrack.Core.Config;

public class CriticSettings
{
    [Range(0, 100)]
    public required double PassingScoreThreshold { get; set; }
    [Range(1, int.MaxValue)]
    public required int MaxAttempts { get; set; }
}