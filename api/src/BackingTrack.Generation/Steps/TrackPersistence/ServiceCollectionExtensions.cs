using BackingTrack.Core.Config;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Steps.TrackPersistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrackPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TrackStorageSettings>()
            .Bind(configuration.GetSection("TrackStorage"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TrackStorageSettings>, TrackStorageSettingsValidator>();

        return services;
    }
}