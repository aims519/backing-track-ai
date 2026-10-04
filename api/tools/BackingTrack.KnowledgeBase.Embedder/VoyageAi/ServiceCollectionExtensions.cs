using BackingTrack.Core.Config;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.KnowledgeBase.Embedder.VoyageAi;

public static class ServiceCollectionExtensions
{
    public static void AddVoyageAiEmbedding(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions();
        services.Configure<VoyageAiSettings>(configuration.GetSection("VoyageAi"));
        services.AddHttpClient<IEmbeddingService, VoyageAiEmbeddingService>(client =>
        {
            client.BaseAddress = new(configuration.GetValue<string>("VoyageAi:RestApiBaseUrl")!);
            client.DefaultRequestHeaders.Authorization = new("Bearer", configuration.GetValue<string>("VoyageAi:ApiKey"));
        })
        .AddStandardResilienceHandler();
    }
}