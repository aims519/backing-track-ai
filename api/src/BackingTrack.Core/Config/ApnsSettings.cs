namespace BackingTrack.Core.Config;

public class ApnsSettings
{
    public required string ServerUrl { get; set; }
    public required string BundleId { get; set; }

    // Secrets absent unless set via user secrets/environment. 
    // If absent, push notifications are disabled using a no-op emitter
    public string? P8KeyId { get; set; }
    public string? P8Key { get; set; }
    public string? TeamId { get; set; }
}