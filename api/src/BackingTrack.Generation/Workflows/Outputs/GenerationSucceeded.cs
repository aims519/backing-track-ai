using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Workflows.Outputs;

public sealed record GenerationSucceeded(Guid TrackId, CandidateTrack CandidateTrack, int Attempts) : GenerationOutcome;