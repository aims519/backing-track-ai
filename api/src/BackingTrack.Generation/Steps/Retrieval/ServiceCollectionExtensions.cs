using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Steps.Retrieval;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeRetrieval(this IServiceCollection services)
    {
        services.AddTransient<IRetrieveStep, RetrieveStep>();
        return services;
    }
}