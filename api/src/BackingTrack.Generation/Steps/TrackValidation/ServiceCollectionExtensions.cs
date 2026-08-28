using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Steps.TrackValidation;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrackValidation(this IServiceCollection services)
    {
        services.AddTransient<ITrackValidator, TrackValidator>();
        services.AddTransient<IValidateStep, ValidateStep>();
        
        return services;
    }
}