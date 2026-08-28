using BackingTrack.Core.Config;

namespace BackingTrack.Api.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        OtelSettings otelSettings = builder.Configuration.GetSection("Otel").Get<OtelSettings>() ?? new OtelSettings();

        builder.Services.AddOtel(otelSettings, builder.Environment);
        builder.Services.AddHealthChecking(otelSettings);
        builder.Logging.AddApplicationLogging(builder.Environment);
        builder.Logging.AddOtelLogging(otelSettings);

        return builder;
    }
}