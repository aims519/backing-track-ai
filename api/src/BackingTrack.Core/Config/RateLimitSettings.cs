namespace BackingTrack.Core.Config;

public class RateLimitSettings
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}