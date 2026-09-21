using BackingTrack.Generation.Jobs;

namespace BackingTrack.Generation.Workflows.Inputs;

public record StatusUpdateInput(Guid JobId, DateTime Timestamp, string Stage, int Attempt, string Message);