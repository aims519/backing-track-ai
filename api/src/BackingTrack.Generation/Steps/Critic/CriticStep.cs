using System.Text.Json;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Steps.Critic;

public class CriticStep([FromKeyedServices("critic")] AIAgent aiAgent, CriticProviderInfo providerInfo) : ICriticStep
{
    public async Task<CriticReview> ReviewCandidateTrackAsync(CandidateTrack candidateTrack, GenerationJob generationJob, MatchedStyle matchedStyle,
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales, CancellationToken cancellationToken = default)
    {
        Task<CriticCriterionResult> rhythmAndFeelTask = ReviewRhythmAndFeelAsync(candidateTrack, generationJob, matchedStyle, matchedChords,
            matchedScales, cancellationToken);

        await Task.WhenAll(rhythmAndFeelTask);
        
        Dictionary<CriticCriterion, CriticCriterionResult> results = [];
        results.Add(CriticCriterion.RhythmAndFeel, rhythmAndFeelTask.Result);

        CriticReview review = new(results);
        return review;
    }

    private async Task<CriticCriterionResult> ReviewRhythmAndFeelAsync(CandidateTrack candidateTrack, GenerationJob generationJob, MatchedStyle matchedStyle,
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales, CancellationToken cancellationToken = default)
    {
        string prompt = GenerateRequestPrompt(CriticCriterion.RhythmAndFeel, candidateTrack, generationJob,
            matchedStyle, matchedChords, matchedScales);
        
        ChatClientAgentRunOptions runOptions = new(new ChatOptions
        {
            MaxOutputTokens = providerInfo.MaxOutputTokens,
        });
        
        AgentResponse<CriticCriterionResult> agentResponse;
        try
        {
            agentResponse = await aiAgent.RunAsync<CriticCriterionResult>(prompt,
                serializerOptions: new JsonSerializerOptions { TypeInfoResolver = CriticStepJsonContext.Default },
                options: runOptions,
                cancellationToken: cancellationToken);
        }
        catch
        {
            throw;
        }
        
        return agentResponse.Result;
    }

    private string GenerateRequestPrompt(CriticCriterion criticCriterion, CandidateTrack candidateTrack,
        GenerationJob generationJob, MatchedStyle matchedStyle,
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales)
    {
        string criterionInstructions = criticCriterion switch
        {
            CriticCriterion.RhythmAndFeel => CriticStepPrompts.CritiqueRhythmAndFeelInstructions,
            _ => string.Empty
        };
        
        return $"""
               - CandidateTrack: {JsonSerializer.Serialize(candidateTrack, CandidateTrackJsonContext.Default.CandidateTrack)}
               - GenerationJob: {JsonSerializer.Serialize(generationJob, GenerationJobJsonContext.Default.GenerationJob)}
               - MatchedStyle: {matchedStyle.StyleEntry.Id} - {matchedStyle.StyleEntry.Description}
               - MatchedChords: {matchedChords.Count.ToString()}
               - MatchedScales: {matchedScales.Count.ToString()}
               - CriticCriterion: {criticCriterion}
               - CriterionDescription: {criterionInstructions}
               """;
    }
    
}