namespace BackingTrack.Core.Config;

public class ApnsSettings
{
    public string ServerUrl { get; set; } = string.Empty;
    public string BundleId { get; set; } = string.Empty;

    // Secrets absent unless set via user secrets/environment. 
    // If absent, push notifications are disabled using a no-op emitter
    public string? P8KeyId { get; set; }
    public string? P8Key { get; set; }
    public string? TeamId { get; set; }
}