using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

using BackingTrack.Api.HealthChecks;
using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackPersistence;
using BackingTrack.Generation.Steps.TrackRepair;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BackingTrack.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOtel(this IServiceCollection services, OtelSettings otelSettings, IWebHostEnvironment environment)
    {
        if (otelSettings.IsEnabled)
        {
            if (string.IsNullOrEmpty(otelSettings.CollectorEndpoint))
                throw new InvalidOperationException("Otel:CollectorEndpoint must be set when Otel:IsEnabled is true");

            services.AddOpenTelemetry()
            .ConfigureResource(resourceBuilder =>
            {
                resourceBuilder.AddService(serviceName: environment.ApplicationName,
                    serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString());
            })
            .WithTracing(tracerProviderBuilder =>
            {
                tracerProviderBuilder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource(TrackGenerationActivitySource.SourceName)
                    .AddSource(GenerationJobActivitySource.SourceName)
                    .AddSource(TrackRepairActivitySource.SourceName)
                    .AddSource(QueryEmbeddingActivitySource.SourceName)
                    .AddSource(TrackPersistenceActivitySource.SourceName)
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otelSettings.CollectorEndpoint);
                    });
            })
            .WithMetrics(metricProviderBuilder =>
            {
                metricProviderBuilder
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter(TrackGenerationMetrics.MeterName)
                    .AddMeter(TrackRepairMetrics.MeterName)
                    .AddMeter(GenerationJobMetrics.MeterName)
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otelSettings.CollectorEndpoint);
                    });
            });

            // Register OTEL collector health check
            if (string.IsNullOrEmpty(otelSettings.CollectorHealthCheckEndpoint))
                throw new InvalidOperationException("Otel:CollectorHealthCheckEndpoint must be set when Otel:IsEnabled is true.");

            services.AddHttpClient<OtelCollectorHealthCheck>(options =>
            {
                options.BaseAddress = new Uri(otelSettings.CollectorHealthCheckEndpoint);
                options.Timeout = TimeSpan.FromSeconds(3);
            });
            services.AddHealthChecks()
                .AddCheck<OtelCollectorHealthCheck>("otelCollector", tags: ["ready"]);
        }

        return services;
    }

    public static IServiceCollection AddHealthChecking(this IServiceCollection services, OtelSettings otelSettings)
    {
        // Default live health check
        services.AddHealthChecks()
            .AddCheck("live", () => HealthCheckResult.Healthy(), tags: ["live"]);

        return services;
    }

    public static IServiceCollection AddRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        RateLimitSettings rateLimitSettings = configuration.GetSection("RateLimit").Get<RateLimitSettings>()!;

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
                logger.LogWarning("Rate limit exceeded for partition {Partition} on {Path}",
                    partitionKey,
                    context.HttpContext.Request.Path);

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers["Retry-After"] = rateLimitSettings.WindowSeconds.ToString();
                await context.HttpContext.Response.WriteAsync("Rate limit exceeded. Please try again later.", cancellationToken);
            };
        });

        return services;
    }

    public static IServiceCollection AddConfigTypes(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions();
        services.Configure<AuthSettings>(configuration.GetSection("Auth"));
        services.Configure<OtelSettings>(configuration.GetSection("Otel"));
        services.Configure<RateLimitSettings>(configuration.GetSection("RateLimit"));
        services.Configure<TrackStorageSettings>(configuration.GetSection("TrackStorage"));
        services.Configure<VoyageAiSettings>(configuration.GetSection("VoyageAi"));
        services.Configure<OpenAiSettings>(configuration.GetSection("OpenAi"));
        services.Configure<TrackGenerationWorkflowSettings>(configuration.GetSection("TrackGenerationWorkflow"));

        return services;
    }

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