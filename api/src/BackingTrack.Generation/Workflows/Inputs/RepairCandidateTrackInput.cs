using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Workflows.Inputs;

public record RepairCandidateTrackInput(CandidateTrack CandidateTrack, TrackValidationResult ValidationResult, GenerationJob GenerationJob,
    IReadOnlyList<MatchedChord> MatchedChords, IReadOnlyList<MatchedScale> MatchedScales, int Attempts);