using System.Text.Json;
using BackingTrack.Generation.Steps.Types;
using Microsoft.Agents.AI;

namespace BackingTrack.Generation.TrackGeneration;

public class AnthropicTrackGenerationClient(AIAgent aiAgent) : ITrackGenerationClient
{
    public async Task<CandidateTrack> GenerateTrackAsync(string prompt, CancellationToken token = default)
    {
        AgentResponse<CandidateTrack> agentResponse = await aiAgent.RunAsync<CandidateTrack>(prompt, serializerOptions: new JsonSerializerOptions { TypeInfoResolver = CandidateTrackJsonContext.Default }, cancellationToken: token);
        
        return agentResponse.Result;
    }
}