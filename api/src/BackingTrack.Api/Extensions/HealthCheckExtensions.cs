using BackingTrack.Api.HealthChecks;
using BackingTrack.Core.Config;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackingTrack.Api.Extensions;

internal static class HealthCheckExtensions
{
    internal static IServiceCollection AddApplicationHealthChecks(this IServiceCollection services, OtelSettings otelSettings)
    {
        // Default live health check
        IHealthChecksBuilder healthChecks = services.AddHealthChecks()
            .AddCheck("live", () => HealthCheckResult.Healthy(), tags: ["live"]);

        if (otelSettings.IsEnabled)
        {
            services.AddHttpClient<OtelCollectorHealthCheck>(client =>
            {
                client.BaseAddress = new Uri(otelSettings.CollectorHealthCheckEndpoint!);
                client.Timeout = TimeSpan.FromSeconds(3);
            });
            healthChecks.AddCheck<OtelCollectorHealthCheck>("otelCollector", tags: ["ready"]);
        }

        return services;
    }
}