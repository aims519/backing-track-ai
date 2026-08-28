namespace BackingTrack.Api.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapApplicationHealthChecks(this IEndpointRouteBuilder endpoints)
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