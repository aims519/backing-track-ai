namespace BackingTrack.Generation.Workflows.Outputs;

public sealed record GenerationFailed(string Reason) : GenerationOutcome;