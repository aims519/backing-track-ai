namespace BackingTrack.Core.Config;

public class ApnsSettings
{
    public required string ServerUrl { get; set; }
    public required string P8Key { get; set; }
    public required string P8KeyId { get; set; }
    public required string TeamId { get; set; }
    public required string BundleId { get; set; }
}