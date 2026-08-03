namespace BackingTrack.Generation.Jobs;

public sealed record GenerationJob
{
    public required Guid Id { get; init; } 
    public required JobStates State { get; init; }
    public required DateTimeOffset StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
    public Guid? TrackId { get; init; }
    public string? AudioLocation { get; init; }
    public required GenerationMode Mode { get; init; }
    public required int Tempo { get; init; }
    public required IReadOnlyList<string> Chords { get; init; }
    public required string Key { get; init; }
    public string? Style { get; init; }
}

public enum GenerationMode
{
    Precise,
    Vibe
}