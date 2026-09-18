using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.FallbackGeneration;

public interface IFallbackGenerationStep
{
    Task<CandidateTrack> GenerateFallbackCandidateTrackAsync(string keyRoot,
        KeyMode keyMode,
        int tempo,
        int timeSignatureBeatCount,
        int timeSignatureBeatUnit,
        MatchedStyle style,
        IReadOnlyList<MatchedChord> chords,
        IReadOnlyList<MatchedScale> scales,
        CancellationToken cancellationToken = default);
}