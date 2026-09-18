using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Workflows.Inputs;

public record ValidateCandidateTrackInput(CandidateTrack CandidateTrack,
    GenerationJob GenerationJob,
    MatchedStyle MatchedStyle,
    IReadOnlyList<MatchedChord> MatchedChords,
    IReadOnlyList<MatchedScale> MatchedScales,
    int ValidationAttempts,
    int CriticAttempts,
    bool IsFallbackTrack = false);