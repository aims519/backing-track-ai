using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Workflows.Inputs;

public record PersistCandidateTrackInput(CandidateTrack CandidateTrack, GenerationJob GenerationJob, int ValidationAttempts, int CriticAttempts, FallbackReason? FallbackReason = null);