using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Workflows.Inputs;

public record GenerateCandidateTrackInput(GenerationJob GenerationJob, MatchedStyle MatchedStyle, IReadOnlyList<MatchedChord> MatchedChords, IReadOnlyList<MatchedScale> MatchedScales, int CriticAttempts,
    CriticFinalReview? CriticReview = null, CandidateTrack? PreviousAttempt = null);