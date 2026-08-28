using System.ClientModel;

using BackingTrack.Core.Config;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using OpenAI;
using OpenAI.Chat;

namespace BackingTrack.Generation.Steps.TrackGeneration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrackGeneration(this IServiceCollection services, IConfiguration configuration)
    {
        string? llmProvider = configuration.GetValue<string>("Llm:Provider");
        if (string.IsNullOrEmpty(llmProvider))
            throw new InvalidOperationException("llmProvider must be set.");

        services.AddKeyedSingleton<AIAgent>("generate", (sp, _) =>
            sp.GetRequiredKeyedService<IChatClient>("generate").AsAIAgent(instructions: GenerateStepPrompts.SystemInstructions));

        services.AddSingleton(sp =>
        {
            OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
            return new TrackGenerationProviderInfo(llmProvider, settings.GenerationModel, settings.GenerationMaxOutputTokens);
        });

        services.AddSingleton<TrackGenerationMetrics>();
        services.AddSingleton<ITrackGenerationClient, TrackGenerationClient>();
        services.AddTransient<IGenerateStep, GenerateStep>();

        return services;
    }
}