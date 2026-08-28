using BackingTrack.Core.Config;

using Microsoft.Extensions.Options;

namespace BackingTrack.Api.Extensions;

internal static class WebApplicationBuilderExtensions
{
    internal static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        OtelSettings otelSettings = builder.Configuration.GetSection("Otel").Get<OtelSettings>() ?? new OtelSettings();
        otelSettings.Validate();

        builder.Services.AddSingleton(Options.Create(otelSettings));
        builder.Services.AddOtel(otelSettings, builder.Environment);
        builder.Services.AddApplicationHealthChecks(otelSettings);
        builder.Logging.AddApplicationLogging(builder.Environment);
        builder.Logging.AddOtelLogging(otelSettings);

        return builder;
    }
}