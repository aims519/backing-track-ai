namespace BackingTrack.Core.Config;

public class OtelSettings
{
    public bool IsEnabled { get; set; }
    public string? CollectorEndpoint { get; set; }
    public string? CollectorHealthCheckEndpoint { get; set; }

    public void Validate()
    {
        if (!IsEnabled) return;
        if (string.IsNullOrEmpty(CollectorEndpoint))
            throw new InvalidOperationException("Otel:CollectorEndpoint must be set when Otel:IsEnabled is true");
        if (string.IsNullOrEmpty(CollectorHealthCheckEndpoint))
            throw new InvalidOperationException("Otel:CollectorHealthCheckEndpoint must be set when Otel:IsEnabled is true");
    }
}