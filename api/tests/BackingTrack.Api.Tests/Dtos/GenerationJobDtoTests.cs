using BackingTrack.Api.Dtos;
using BackingTrack.Generation.Jobs;

namespace BackingTrack.Api.Tests.Dtos;

public class GenerationJobDtoTests
{
    [Fact]
    public void ToDto_MapsFeedbackItemsFieldByField()
    {
        DateTime timestamp = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        GenerationJob job = new()
        {
            Id = Guid.NewGuid(),
            State = JobStates.InProgress,
            StartTime = DateTimeOffset.UtcNow,
            Mode = GenerationMode.Precise,
            Tempo = 120,
            Chords = ["C", "G", "Am", "F"],
            KeyRoot = "C",
            KeyMode = KeyMode.Ionian,
            Style = "rock",
            TimeSignatureBeatCount = 4,
            TimeSignatureBeatUnit = 4,
            FeedbackItems =
            [
                new JobFeedback(timestamp, "Validating", "Validating track (attempt 1)", 1)
            ]
        };

        GenerationJobDto dto = job.ToDto();

        JobFeedbackDto feedback = Assert.Single(dto.FeedbackItems);
        Assert.Equal(timestamp, feedback.Timestamp);
        Assert.Equal("Validating", feedback.Stage);
        Assert.Equal("Validating track (attempt 1)", feedback.Message);
        Assert.Equal(1, feedback.Attempt);
    }
}