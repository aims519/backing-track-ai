using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.Critic;

public class CriticStep(ICriticAiClient criticAiClient) : ICriticStep
{
    public async Task<CriticFinalReview> ReviewCandidateTrackAsync(CandidateTrack candidateTrack, GenerationJob generationJob, MatchedStyle matchedStyle,
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales, CancellationToken cancellationToken = default)
    {
        CriticVerdictSubmission verdictSubmission = await criticAiClient.GenerateCriticVertictSubmissionAsync(candidateTrack, generationJob, matchedStyle, matchedChords, matchedScales, cancellationToken);
        
        bool isPass = false;
        IReadOnlyList<CriticCitation> citations = [];
        double score = 0d;
        
        CriticFinalReview finalReview = new(isPass, score, citations);
        return finalReview;
    }
    
}