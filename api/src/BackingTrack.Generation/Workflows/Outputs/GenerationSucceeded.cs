using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Workflows.Outputs;

public sealed record GenerationSucceeded(Guid TrackId, CandidateTrack CandidateTrack, int Attempts, FallbackReason? FallbackReason = null, int CriticAttempts = 0) : GenerationOutcome;