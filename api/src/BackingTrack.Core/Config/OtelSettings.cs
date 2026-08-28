namespace BackingTrack.Core.Config;

public class OtelSettings
{
    public bool IsEnabled { get; set; }
    public string? CollectorEndpoint { get; set; }
    public string? CollectorHealthCheckEndpoint { get; set; }
}