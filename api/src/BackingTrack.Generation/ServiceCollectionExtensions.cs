using System.ClientModel;
using System.Threading.Channels;

using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Workflows.Executors;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using OpenAI;

namespace BackingTrack.Generation;

public static class ServiceCollectionExtensions
{
    // OpenAI's own guidance for Flex processing: its variable, often-slower queueing routinely
    // exceeds the SDK's much shorter default network timeout, so give Flex-tier steps more room.
    // See https://developers.openai.com/api/docs/guides/flex-processing.
    private static readonly TimeSpan FlexNetworkTimeout = TimeSpan.FromMinutes(15);

    private static IChatClient CreateChatClient(OpenAiSettings settings, string model, OpenAiApiKind apiKind, OpenAiServiceTier? serviceTier)
    {
        OpenAIClient client = new(new ApiKeyCredential(settings.ApiKey), new OpenAIClientOptions()
        {
            Endpoint = new Uri(settings.BaseUrl),
            NetworkTimeout = serviceTier == OpenAiServiceTier.Flex ? FlexNetworkTimeout : null,
        });

#pragma warning disable OPENAI001 // Responses-API IChatClient adapter is still evaluation-only upstream.
        return apiKind switch
        {
            OpenAiApiKind.Responses => client.GetResponsesClient().AsIChatClient(model),
            OpenAiApiKind.ChatCompletions => client.GetChatClient(model).AsIChatClient(),
            _ => throw new InvalidOperationException($"OpenAi API kind '{apiKind}' is not supported."),
#pragma warning restore OPENAI001
        };
    }

    public static IServiceCollection AddLlmChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        string? llmProvider = configuration.GetValue<string>("Llm:Provider");
        if (string.IsNullOrEmpty(llmProvider))
            throw new InvalidOperationException("llmProvider must be set.");

        if (llmProvider == "OpenAi")
        {
            services.Configure<OpenAiSettings>(configuration.GetSection("OpenAi"));
            services.AddKeyedSingleton<IChatClient>("generate", (sp, _) =>
            {
                OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
                return CreateChatClient(settings, settings.GenerationModel, settings.GenerationApi, settings.GenerationServiceTier);
            });

            services.AddKeyedSingleton<IChatClient>("repair", (sp, _) =>
            {
                OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
                return CreateChatClient(settings, settings.RepairModel, settings.RepairApi, settings.RepairServiceTier);
            });

            services.AddKeyedSingleton<IChatClient>("critic", (sp, _) =>
            {
                OpenAiSettings settings = sp.GetRequiredService<IOptions<OpenAiSettings>>().Value;
                return CreateChatClient(settings, settings.CriticModel, settings.CriticApi, settings.CriticServiceTier);
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
        services.AddScoped<FallbackGenerateCandidateTrackExecutor>();
        services.AddScoped<RepairCandidateTrackExecutor>();
        services.AddScoped<CriticExecutor>();
        services.AddScoped<PersistCandidateTrackExecutor>();
        services.AddScoped<StatusUpdateExecutor>();

        return services;
    }

    public static IServiceCollection AddJobProcessing(this IServiceCollection services)
    {
        services.AddSingleton<JobStore>();

        Channel<Guid> jobChannel = Channel.CreateUnbounded<Guid>();
        services.AddSingleton(new GenerationJobQueue(jobChannel));
        services.AddSingleton<GenerationJobMetrics>();
        services.AddHostedService<GenerationJobWorkerHostedService>();

        return services;
    }
}