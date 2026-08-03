using BackingTrack.Generation.Jobs;

namespace BackingTrack.Api.Dtos;

public class GenerationJobDto
{
    public required Guid Id { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset? StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
    public Guid? TrackId { get; init; }
    public string? AudioLocation { get; init; }
    public required string Mode { get; init; }
    public required int Tempo { get; init; }
    public required IReadOnlyList<string> Chords { get; init; }
    public required string Key { get; init; }
}

internal static class GenerationJobDtoExtensions
{
    public static GenerationJobDto ToDto(this GenerationJob job) => new()
    {
        Id = job.Id,
        Status = job.State.ToString(),
        StartTime = job.StartTime,
        EndTime = job.EndTime,
        TrackId = job.TrackId,
        AudioLocation = job.AudioLocation,
        Mode = job.Mode.ToString(),
        Tempo = job.Tempo,
        Chords = job.Chords,
        Key = job.Key
    };
}