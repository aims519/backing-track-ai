using System.ClientModel;

using BackingTrack.Core.Config;
using BackingTrack.Generation.Workflows.Executors;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using OpenAI;
using OpenAI.Chat;

namespace BackingTrack.Generation;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLlmChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        string? llmProvider = configuration.GetValue<string>("Llm:Provider");
        if (string.IsNullOrEmpty(llmProvider))
            throw new InvalidOperationException("llmProvider must be set.");
        if (llmProvider == "OpenAi")
        {
            services.AddKeyedSingleton<IChatClient>("generate", (sp, _) =>
            {
                OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
                ChatClient client = new(settings.GenerationModel, new ApiKeyCredential(settings.ApiKey), new OpenAIClientOptions()
                {
                    Endpoint = new Uri(settings.BaseUrl),
                });
                return client.AsIChatClient();
            });

            services.AddKeyedSingleton<IChatClient>("repair", (sp, _) =>
            {
                OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
                ChatClient client = new(settings.RepairModel, new ApiKeyCredential(settings.ApiKey), new OpenAIClientOptions()
                {
                    Endpoint = new Uri(settings.BaseUrl),
                });
                return client.AsIChatClient();
            });
        }
        else
        {
            throw new InvalidOperationException($"Llm:Provider value, '{llmProvider}', is not supported");
        }

        return services;
    }

    public static IServiceCollection AddTrackGenerationWorkflow(this IServiceCollection services)
    {
        services.AddScoped<RetrieveStyleExecutor>();
        services.AddScoped<RetrieveChordsExecutor>();
        services.AddScoped<RetrieveScalesExecutor>();
        services.AddScoped<GenerateCandidateTrackExecutor>();
        services.AddScoped<ValidateCandidateTrackExecutor>();
        services.AddScoped<RepairCandidateTrackExecutor>();
        services.AddScoped<PersistCandidateTrackExecutor>();

        return services;
    }
}