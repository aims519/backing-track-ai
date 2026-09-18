using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Steps.FallbackGeneration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFallbackTrackGeneration(this IServiceCollection services)
    {
        services.AddTransient<IFallbackGenerationStep, FallbackGenerationStep>();

        return services;
    }
}