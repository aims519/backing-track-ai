using BackingTrack.Core.Config;

namespace BackingTrack.Api.Extensions;

internal static class OptionsExtensions
{
    internal static IServiceCollection AddAppOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitSettings>(configuration.GetSection("RateLimit"));

        services.AddOptions<AuthSettings>()
            .Bind(configuration.GetSection("Auth"))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "Auth:ApiKey must be configured")
            .ValidateOnStart();

        return services;
    }
}