using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using System.Threading.RateLimiting;

using BackingTrack.Api.Auth;
using BackingTrack.Api.Dtos;
using BackingTrack.Api.Endpoints;
using BackingTrack.Api.Errors;
using BackingTrack.Api.HealthChecks;
using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Embedding.VoyageAI;
using BackingTrack.Generation.Steps;
using BackingTrack.Generation.TrackGeneration;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddLogging();

builder.Services.AddOptions();
builder.Services.Configure<AuthSettings>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<OtelSettings>(builder.Configuration.GetSection("Otel"));
builder.Services.Configure<RateLimitSettings>(builder.Configuration.GetSection("RateLimit"));
builder.Services.Configure<TrackStorageSettings>(builder.Configuration.GetSection("TrackStorage"));
builder.Services.Configure<VoyageAISettings>(builder.Configuration.GetSection("VoyageAI"));
builder.Services.Configure<OpenAiSettings>(builder.Configuration.GetSection("OpenAi"));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Minimal APIs only throw on JSON body-binding failures when IsDevelopment() by default;
// force it in all environments so GlobalExceptionHandler sees a consistent BadHttpRequestException
// rather than a bare status-only 400 in Production.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
    });
}
else
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
    });
}

OtelSettings otelSettings = builder.Configuration.GetSection("Otel").Get<OtelSettings>() ?? new OtelSettings();
if (otelSettings.bEnabled)
{
    if(string.IsNullOrEmpty(otelSettings.CollectorEndpoint))
        throw new InvalidOperationException("Otel:CollectorEndpoint must be set when Otel:bEnabled is true.");

    builder.Services.AddOpenTelemetry()
    .ConfigureResource(resourceBuilder =>
    {
        resourceBuilder.AddService(serviceName: builder.Environment.ApplicationName,
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString());
    })
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource(TrackGenerationActivitySource.SourceName)
            .AddSource(GenerationJobActivitySource.SourceName)
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
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otelSettings.CollectorEndpoint);
            });
    });

    builder.Logging.AddOpenTelemetry(options =>
    {
        options.IncludeScopes = true;
        options.IncludeFormattedMessage = true;
        options.AddOtlpExporter(otlpExporterOptions =>
        {
            otlpExporterOptions.Endpoint = new Uri(otelSettings.CollectorEndpoint);
        });
    });
    
    // Register OTEL collector health check
    if(string.IsNullOrEmpty(otelSettings.CollectorHealthCheckEndpoint))
        throw new InvalidOperationException("Otel:CollectorHealthCheckEndpoint must be set when Otel:bEnabled is true.");

    builder.Services.AddHttpClient<OtelCollectorHealthCheck>(options =>
    {
        options.BaseAddress = new Uri(otelSettings.CollectorHealthCheckEndpoint);
        options.Timeout =  TimeSpan.FromSeconds(3);
    });
    builder.Services.AddHealthChecks()
        .AddCheck<OtelCollectorHealthCheck>("otelCollector", tags: ["ready"]);
}

// Default live health check
builder.Services.AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy(), tags: ["live"]);

RateLimitSettings rateLimitSettings = builder.Configuration.GetSection("RateLimit").Get<RateLimitSettings>()!;

static string GetPartitionKey(HttpContext context)
{
    string? apiKey = context.Request.Headers["X-API-Key"]; 
    return string.IsNullOrEmpty(apiKey) ? "anonymous" : HashApiKey(apiKey);     
}
static string HashApiKey(string apiKey)
{
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
    return Convert.ToHexString(hash)[..8];
}
builder.Services.AddRateLimiter(rateLimitOptions =>
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

builder.Services.AddSingleton<JobStore>();

Channel<Guid> jobChannel = Channel.CreateUnbounded<Guid>();
builder.Services.AddSingleton(new GenerationJobQueue(jobChannel));
builder.Services.AddHostedService<GenerationJobWorkerHostedService>();
builder.Services.AddTransient<IRetrieveStep, RetrieveStep>();

// Vector Store Initialisation
builder.Services.AddKnowledgeBaseVectorStore();

builder.Services.AddSingleton<ChordSymbolParser>();
builder.Services.AddSingleton<ScaleMatcher>();

builder.Services.AddHttpClient<IQueryEmbeddingService, VoyageAiQueryEmbeddingService>(client =>
{
    client.BaseAddress = new(builder.Configuration.GetValue<string>("VoyageAI:RestApiBaseUrl")!);
    client.DefaultRequestHeaders.Authorization = new("Bearer", builder.Configuration.GetValue<string>("VoyageAI:ApiKey"));
});

// Track Generation
builder.Services.AddTrackGeneration(builder.Configuration);
builder.Services.AddTransient<IGenerateStep, GenerateStep>();

WebApplication app = builder.Build();
app.Services.GetRequiredService<InMemoryVectorStore>(); // forces eager load + logging

app.UseExceptionHandler();

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/healthz/ready", new()
{
    Predicate = healthCheck => healthCheck.Tags.Contains("ready")
});

app.MapHealthChecks("/healthz/live", new()
{
    Predicate = healthCheck => healthCheck.Tags.Contains("live")
});

RouteGroupBuilder trackEndpoints = app.MapGroup("/tracks")
    .AddEndpointFilter<ApiKeyFilter>()
    .RequireRateLimiting("fixed")
    .MapTrackEndpoints();

app.Run();

[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(ValidationProblemDetails))]
[JsonSerializable(typeof(GenerationJobDto))]
[JsonSerializable(typeof(GenerationRequestDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
