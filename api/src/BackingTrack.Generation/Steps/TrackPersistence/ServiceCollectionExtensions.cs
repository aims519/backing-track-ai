using BackingTrack.Core.Config;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Steps.TrackPersistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrackPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TrackStorageSettings>(configuration.GetSection("TrackStorage"));

        return services;
    }
}