using System.ComponentModel.DataAnnotations;

namespace BackingTrack.Core.Config;

public class JobEvictionSettings
{
    [Range(1, int.MaxValue)]
    public int SweepIntervalSeconds { get; set; }
    [Range(1, int.MaxValue)]
    public int JobTtlSeconds { get; set; }
}