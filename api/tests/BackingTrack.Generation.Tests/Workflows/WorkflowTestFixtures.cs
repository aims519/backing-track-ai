using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Tests.Workflows;

/// <summary>
/// Shared builders for the domain objects threaded through the generation workflow's executors,
/// so each executor's test file doesn't have to re-declare its own copy.
/// </summary>
internal static class WorkflowTestFixtures
{
    public static GenerationJob CreateGenerationJob() => new()
    {
        Id = Guid.NewGuid(),
        State = JobStates.Pending,
        StartTime = DateTimeOffset.UtcNow,
        Mode = GenerationMode.Precise,
        Tempo = 120,
        Chords = ["C", "G", "Am", "F"],
        KeyRoot = "C",
        KeyMode = KeyMode.Ionian,
        Style = "rock",
        TimeSignatureBeatCount = 4,
        TimeSignatureBeatUnit = 4,
    };

    public static MatchedStyle CreateMatchedStyle() =>
        new(new StyleEntry { Id = "rock", Description = "Driving rock" });

    public static MatchedChord CreateMatchedChord(string root = "C") =>
        new(new ChordEntry { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" }, root);

    public static MatchedScale CreateMatchedScale(MatchedChord chord) =>
        new(new ScaleEntry { Id = "major", Intervals = [0, 2, 4, 5, 7, 9, 11], Description = "Major scale" }, chord);
}