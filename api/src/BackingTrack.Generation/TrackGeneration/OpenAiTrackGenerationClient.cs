using System.Text.Json;

using BackingTrack.Generation.Steps.Types;

using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.TrackGeneration;

public class OpenAiTrackGenerationClient(AIAgent aiAgent, ILogger<OpenAiTrackGenerationClient> logger) : ITrackGenerationClient
{
    public async Task<CandidateTrack> GenerateTrackAsync(string prompt, CancellationToken token = default)
    {
        AgentResponse<CandidateTrack> agentResponse = await aiAgent.RunAsync<CandidateTrack>(prompt, serializerOptions: new JsonSerializerOptions { TypeInfoResolver = CandidateTrackJsonContext.Default }, cancellationToken: token);
        
        logger.LogInformation("Generated Candidate Track. Input tokens: {InputCount}. Output tokens: {OutputCount}", 
            agentResponse.Usage?.InputTokenCount, agentResponse.Usage?.OutputTokenCount);
        
        return agentResponse.Result;
    }
}