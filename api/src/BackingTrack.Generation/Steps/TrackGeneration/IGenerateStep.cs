using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackGeneration;

public interface IGenerateStep
{
    Task<CandidateTrack> GenerateCandidateTrackAsync(string keyRoot,
        int tempo,
        int timeSignatureBeatCount,
        int timeSignatureBeatUnit,
        MatchedStyle style, 
        IReadOnlyList<MatchedChord> chords,
        IReadOnlyList<MatchedScale> scales, 
        CandidateTrack? previousAttempt = null,
        IReadOnlyList<ValidationFailure>? previousAttemptFailures = null,
        CancellationToken cancellationToken = default);
}