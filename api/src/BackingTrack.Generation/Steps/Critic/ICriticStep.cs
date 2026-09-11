using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.Critic;

public interface ICriticStep
{
    Task<CriticReview> ReviewCandidateTrackAsync(CandidateTrack candidateTrack, GenerationJob generationJob,
        MatchedStyle matchedStyle, IReadOnlyList<MatchedChord> matchedChords,
        IReadOnlyList<MatchedScale> matchedScales, CancellationToken cancellationToken = default);
}