using System.ClientModel;

using BackingTrack.Core.Config;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using OpenAI;
using OpenAI.Chat;

namespace BackingTrack.Generation.TrackGeneration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrackGeneration(this IServiceCollection services, IConfiguration configuration)
    {
        string? llmProvider = configuration.GetValue<string>("Llm:Provider");
        if(string.IsNullOrEmpty(llmProvider))
            throw new InvalidOperationException("llmProvider must be set.");
        if (llmProvider == "OpenAi")
        {
            services.AddSingleton<IChatClient>(sp =>
            {
                OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
                ChatClient client = new(settings.Model, new ApiKeyCredential(settings.ApiKey),new OpenAIClientOptions()
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

        services.AddSingleton<AIAgent>(sp =>
            sp.GetRequiredService<IChatClient>().AsAIAgent(instructions: GenerateStepPrompts.SystemInstructions));

        services.AddSingleton(sp =>
        {
            OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
            return new TrackGenerationProviderInfo(llmProvider, settings.Model, settings.MaxOutputTokens);
        });

        services.AddSingleton<TrackGenerationMetrics>();
        services.AddSingleton<ITrackGenerationClient, TrackGenerationClient>();

        return services;
    }
}