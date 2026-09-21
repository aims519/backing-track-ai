using BackingTrack.Generation.Jobs;

namespace BackingTrack.Api.Dtos;

public sealed record GenerationJobDto
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
    public required string KeyRoot { get; init; }
    public required string KeyMode { get; init; }
    public IReadOnlyList<string> KeyModeWarnings { get; init; } = [];
    public required int TimeSignatureBeatCount { get; init; }
    public required int TimeSignatureBeatUnit { get; init; }
    public required IReadOnlyList<JobFeedbackDto> FeedbackItems { get; init; }
}

public sealed record JobFeedbackDto(DateTime Timestamp, string Stage, string Message, int Attempt);

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
        KeyRoot = job.KeyRoot,
        KeyMode = job.KeyMode.ToString(),
        KeyModeWarnings = job.KeyModeWarnings,
        TimeSignatureBeatCount = job.TimeSignatureBeatCount,
        TimeSignatureBeatUnit = job.TimeSignatureBeatUnit,
        FeedbackItems = job.FeedbackItems.Select(i => new JobFeedbackDto(i.Timestamp, i.Stage, i.Message, i.Attempt)).ToList(),
    };
}