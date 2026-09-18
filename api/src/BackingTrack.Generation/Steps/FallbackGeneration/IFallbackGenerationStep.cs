using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.FallbackGeneration;

public interface IFallbackGenerationStep
{
    Task<CandidateTrack> GenerateFallbackCandidateTrackAsync(
        int timeSignatureBeatCount,
        int timeSignatureBeatUnit,
        MatchedStyle style,
        IReadOnlyList<MatchedChord> chords,
        CancellationToken cancellationToken = default);
}