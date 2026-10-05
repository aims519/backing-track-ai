using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackPersistence;
using BackingTrack.Generation.Steps.TrackRepair;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BackingTrack.Api.Extensions;

internal static class OpenTelemetryExtensions
{
    internal static IServiceCollection AddOtel(this IServiceCollection services, OtelSettings otelSettings, IWebHostEnvironment environment)
    {
        if (otelSettings.IsEnabled)
        {
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
                    .AddSource(CriticActivitySource.SourceName)
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otelSettings.CollectorEndpoint!);
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
                    .AddMeter(CriticMetrics.MeterName)
                    .AddMeter("Polly")
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otelSettings.CollectorEndpoint!);
                    });
            });
        }

        return services;
    }
}