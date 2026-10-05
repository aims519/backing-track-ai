using BackingTrack.Core.Config;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Steps.Critic;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCritic(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CriticSettings>()
            .Bind(configuration.GetSection("Critic"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CriticSettings>, CriticSettingsValidator>();

        string? llmProvider = configuration.GetValue<string>("Llm:Provider");
        if (string.IsNullOrEmpty(llmProvider))
            throw new InvalidOperationException("llmProvider must be set.");

        services.AddSingleton(sp =>
        {
            OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
            return new CriticProviderInfo(llmProvider, settings.CriticModel, settings.CriticMaxOutputTokens, settings.CriticServiceTier);
        });

        services.AddTransient<ICriticAiClient, CriticAiClient>();
        services.AddTransient<ICriticStep, CriticStep>();
        services.AddSingleton<CriticMetrics>();

        return services;
    }
}