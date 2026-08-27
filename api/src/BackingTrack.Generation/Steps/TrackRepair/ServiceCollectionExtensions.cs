using System.ClientModel;

using BackingTrack.Core.Config;
using BackingTrack.Generation.Steps.TrackGeneration;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using OpenAI;
using OpenAI.Chat;

namespace BackingTrack.Generation.Steps.TrackRepair;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrackRepair(this IServiceCollection services, IConfiguration configuration)
    {
        string? llmProvider = configuration.GetValue<string>("Llm:Provider");
        if(string.IsNullOrEmpty(llmProvider))
            throw new InvalidOperationException("llmProvider must be set.");

        services.AddKeyedSingleton<AIAgent>("repair", (sp, _) =>
            sp.GetRequiredKeyedService<IChatClient>("repair").AsAIAgent(instructions: RepairStepPrompts.SystemInstructions));

        services.AddSingleton(sp =>
        {
            OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
            return new TrackRepairProviderInfo(llmProvider, settings.RepairModel, settings.RepairMaxOutputTokens);
        });

        services.AddSingleton<TrackRepairMetrics>();

        return services;
    }
}