using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackGeneration;

public interface IGenerateStep
{
    Task<CandidateTrack> GenerateCandidateTrackAsync(string keyRoot,
        KeyMode keyMode,
        int tempo,
        int timeSignatureBeatCount,
        int timeSignatureBeatUnit,
        MatchedStyle style,
        IReadOnlyList<MatchedChord> chords,
        IReadOnlyList<MatchedScale> scales,
        CancellationToken cancellationToken = default);

    Task<CandidateTrack> GenerateCandidateTrackWithCriticReviewAsync(string keyRoot,
        KeyMode keyMode,
        int tempo,
        int timeSignatureBeatCount,
        int timeSignatureBeatUnit,
        MatchedStyle style,
        IReadOnlyList<MatchedChord> chords,
        IReadOnlyList<MatchedScale> scales,
        CandidateTrack previousAttempt,
        CriticFinalReview criticReview,
        CancellationToken cancellationToken = default);
}