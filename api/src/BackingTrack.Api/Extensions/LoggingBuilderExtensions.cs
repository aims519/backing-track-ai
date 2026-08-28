using BackingTrack.Core.Config;

using OpenTelemetry.Logs;

namespace BackingTrack.Api.Extensions;

internal static class LoggingBuilderExtensions
{
    internal static ILoggingBuilder AddOtelLogging(this ILoggingBuilder loggingBuilder, OtelSettings otelSettings)
    {
        if (!otelSettings.IsEnabled)
            return loggingBuilder;

        if (string.IsNullOrEmpty(otelSettings.CollectorEndpoint))
            throw new InvalidOperationException("Otel:CollectorEndpoint must be set when Otel:IsEnabled is true");

        loggingBuilder.AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
            options.AddOtlpExporter(otlpExporterOptions =>
            {
                otlpExporterOptions.Endpoint = new Uri(otelSettings.CollectorEndpoint);
            });
        });

        return loggingBuilder;
    }

    internal static ILoggingBuilder AddApplicationLogging(
        this ILoggingBuilder builder, IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            builder.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
                options.UseUtcTimestamp = true;
            });
        }
        else
        {
            builder.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
                options.UseUtcTimestamp = true;
            });
        }

        return builder;
    }
}