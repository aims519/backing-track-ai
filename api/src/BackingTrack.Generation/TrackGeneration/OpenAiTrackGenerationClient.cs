using System.Text.Json;

using BackingTrack.Core.Config;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.TrackGeneration;

public class OpenAiTrackGenerationClient(AIAgent aiAgent, IOptions<OpenAiSettings> settings, ILogger<OpenAiTrackGenerationClient> logger) : ITrackGenerationClient
{
    public async Task<CandidateTrack> GenerateTrackAsync(string prompt, CancellationToken token = default)
    {
        ChatClientAgentRunOptions runOptions = new(new ChatOptions
        {
            MaxOutputTokens = settings.Value.MaxOutputTokens
        });
        
        AgentResponse<CandidateTrack> agentResponse = await aiAgent.RunAsync<CandidateTrack>(prompt, 
            serializerOptions: new JsonSerializerOptions { TypeInfoResolver = CandidateTrackJsonContext.Default }, 
            options: runOptions,
            cancellationToken: token);
        
        logger.LogInformation("Generated Candidate Track. Input tokens: {InputCount}. Output tokens: {OutputCount}", 
            agentResponse.Usage?.InputTokenCount, agentResponse.Usage?.OutputTokenCount);
        
        return agentResponse.Result;
    }
}