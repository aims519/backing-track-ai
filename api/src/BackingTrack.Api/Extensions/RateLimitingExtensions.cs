using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

using BackingTrack.Core.Config;

namespace BackingTrack.Api.Extensions;

internal static class RateLimitingExtensions
{
    internal static IServiceCollection AddRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        RateLimitSettings rateLimitSettings = configuration.GetSection("RateLimit").Get<RateLimitSettings>() ?? throw new InvalidOperationException("Rate limit settings not provided");

        services.AddRateLimiter(rateLimitOptions =>
        {
            rateLimitOptions.AddPolicy("fixed", context =>
            {
                string partitionKey = GetPartitionKey(context);

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitSettings.PermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitSettings.WindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,
                });
            });

            rateLimitOptions.OnRejected = async (context, cancellationToken) =>
            {
                // Custom rejection handling logic
                ILogger logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting");

                string partitionKey = GetPartitionKey(context.HttpContext);
                logger.LogWarning("Rate limit exceeded for partition {Partition} on {Route}",
                    partitionKey,
                    GetRouteTemplate(context.HttpContext));

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers["Retry-After"] = rateLimitSettings.WindowSeconds.ToString();
                await context.HttpContext.Response.WriteAsync("Rate limit exceeded. Please try again later.", cancellationToken);
            };
        });

        return services;
    }

    // The matched route's template, not Request.Path: the raw path is caller-supplied and can carry a
    // decoded CR/LF into a plain-text log sink (CodeQL cs/log-forging). The template is fixed by our own code.
    internal static string GetRouteTemplate(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown";

    private static string GetPartitionKey(HttpContext context)
    {
        string? apiKey = context.Request.Headers["X-API-Key"];
        return string.IsNullOrEmpty(apiKey) ? "anonymous" : HashApiKey(apiKey);
    }

    private static string HashApiKey(string apiKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash)[..8];
    }
}