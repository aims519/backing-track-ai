using BackingTrack.Core.Config;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Steps.Critic;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCritic(this IServiceCollection services, IConfiguration configuration)
    {
        string? llmProvider = configuration.GetValue<string>("Llm:Provider");
        if (string.IsNullOrEmpty(llmProvider))
            throw new InvalidOperationException("llmProvider must be set.");

        services.AddKeyedSingleton<AIAgent>("critic", (sp, _) =>
            sp.GetRequiredKeyedService<IChatClient>("critic").AsAIAgent(instructions:  CriticStepPrompts.SystemInstructions));
        
        services.AddSingleton(sp =>
        {
            OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
            return new CriticProviderInfo(llmProvider, settings.CriticModel, settings.CriticMaxOutputTokens);
        });
        
        services.AddTransient<ICriticStep, CriticStep>();

        return services;
    }
}