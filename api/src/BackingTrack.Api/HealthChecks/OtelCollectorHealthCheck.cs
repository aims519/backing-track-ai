using BackingTrack.Core.Config;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BackingTrack.Api.HealthChecks;

internal class OtelCollectorHealthCheck(IOptions<OtelSettings> otelSettings, HttpClient httpClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!otelSettings.Value.bEnabled)
            return HealthCheckResult.Healthy("A healthy result.");

        HttpResponseMessage response = await httpClient.GetAsync("/healthz/status", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return HealthCheckResult.Unhealthy("OTEL collector check failed");

        string responseString = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.Equals(responseString, "healthy", StringComparison.OrdinalIgnoreCase)
            ? HealthCheckResult.Healthy("OTEL collector check passed")
            : HealthCheckResult.Unhealthy("OTEL collector check failed");
    }
}