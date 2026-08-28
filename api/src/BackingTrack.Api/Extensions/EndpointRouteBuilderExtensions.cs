namespace BackingTrack.Api.Extensions;

internal static class EndpointRouteBuilderExtensions
{
    internal static IEndpointRouteBuilder MapApplicationHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/healthz/ready", new()
        {
            Predicate = healthCheck => healthCheck.Tags.Contains("ready")
        });

        endpoints.MapHealthChecks("/healthz/live", new()
        {
            Predicate = healthCheck => healthCheck.Tags.Contains("live")
        });

        return endpoints;
    }
}