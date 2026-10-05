using System.Diagnostics;
using System.Text.Json;

using BackingTrack.Generation.Steps.Types;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Steps.TrackGeneration;

public class TrackGenerationClient(
    [FromKeyedServices("generate")] AIAgent aiAgent,
    TrackGenerationProviderInfo providerInfo,
    TrackGenerationMetrics metrics,
    ILogger<TrackGenerationClient> logger) : ITrackGenerationClient
{
    public async Task<CandidateTrack> GenerateTrackAsync(string prompt, CancellationToken token = default)
    {
        using Activity? activity = TrackGenerationActivitySource.Instance.StartActivity($"{nameof(GenerateStep)}.{nameof(TrackGenerationClient)}.GenerateTrackAsync");
        activity?.SetTag("track_generation.provider", providerInfo.Provider);
        activity?.SetTag("track_generation.model", providerInfo.Model);
        activity?.SetTag("track_generation.service_tier", providerInfo.ServiceTier.ToMetricTag());

        ChatOptions chatOptions = new()
        {
            MaxOutputTokens = providerInfo.MaxOutputTokens
        };
        chatOptions.ApplyServiceTier(providerInfo.ServiceTier);
        ChatClientAgentRunOptions runOptions = new(chatOptions);

        Stopwatch stopwatch = Stopwatch.StartNew();
        AgentResponse<CandidateTrack> agentResponse;
        try
        {
            agentResponse = await aiAgent.RunAsync<CandidateTrack>(prompt,
                serializerOptions: new JsonSerializerOptions { TypeInfoResolver = CandidateTrackJsonContext.Default },
                options: runOptions,
                cancellationToken: token);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            metrics.RecordFailure(providerInfo.Provider,
                providerInfo.Model,
                providerInfo.ServiceTier.ToMetricTag(),
                stopwatch.Elapsed);
            throw;
        }
        stopwatch.Stop();

        logger.LogInformation("Generated Candidate Track. Input tokens: {InputCount}. Output tokens: {OutputCount}",
            agentResponse.Usage?.InputTokenCount, agentResponse.Usage?.OutputTokenCount);

        activity?.SetTag("track_generation.input_tokens", agentResponse.Usage?.InputTokenCount);
        activity?.SetTag("track_generation.output_tokens", agentResponse.Usage?.OutputTokenCount);
        metrics.RecordSuccess(providerInfo.Provider,
            providerInfo.Model,
            providerInfo.ServiceTier.ToMetricTag(),
            stopwatch.Elapsed,
            agentResponse.Usage?.InputTokenCount,
            agentResponse.Usage?.OutputTokenCount);

        return agentResponse.Result;
    }
}