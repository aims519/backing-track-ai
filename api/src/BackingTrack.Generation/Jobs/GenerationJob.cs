namespace BackingTrack.Generation.Jobs;

public sealed record GenerationJob
{
    public required Guid Id { get; init; } 
    public required JobStates State { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
}