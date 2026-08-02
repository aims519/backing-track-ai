namespace BackingTrack.Api.Dtos;

public class GenerationJobDto
{
    public required Guid Id { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset? StartTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
}