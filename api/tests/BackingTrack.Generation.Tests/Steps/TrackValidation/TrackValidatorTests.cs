using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Tests.Steps.TrackValidation;

public class TrackValidatorTests
{
    private static TrackValidator CreateTrackValidator() => new(new ScaleMatcher());

    private static GenerationJob CreateGenerationJob(IReadOnlyList<string>? chords = null, int timeSignatureBeatCount = 4) => new()
    {
        Id = Guid.NewGuid(),
        State = JobStates.Pending,
        StartTime = DateTimeOffset.UtcNow,
        Mode = GenerationMode.Precise,
        Tempo = 120,
        Chords = chords ?? ["C", "G", "Am", "F"],
        KeyRoot = "C",
        Style = "rock",
        TimeSignatureBeatCount = timeSignatureBeatCount,
        TimeSignatureBeatUnit = 4,
    };

    private static CandidateTrack CreateCandidateTrack(double lengthInBeats) =>
        new(lengthInBeats, [], [], []);

    [Theory]
    [InlineData(new[] { "C", "G", "Am", "F" }, 4, 16)]
    [InlineData(new[] { "C", "G", "Am", "F" }, 3, 12)]
    [InlineData(new[] { "C", "G", "Am", "F", "Em", "G" }, 7, 42)]
    public void ValidateLengthInBeats_LengthMatchesBeatsPerBarTimesChordCount_ReturnsNoFailures(string[] chords, int timeSignatureBeatCount, int expectedLengthInBeats)
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: chords, timeSignatureBeatCount: timeSignatureBeatCount);
        CandidateTrack candidateTrack = CreateCandidateTrack(lengthInBeats: expectedLengthInBeats);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateLengthInBeats(candidateTrack, generationJob);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateLengthInBeats_LengthShorterThanExpected_ReturnsFailureWithExpectedAndActualLength()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4);
        CandidateTrack candidateTrack = CreateCandidateTrack(lengthInBeats: 12);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateLengthInBeats(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Contains("16", failure.Reason);
        Assert.Contains("12", failure.Reason);
    }

    [Fact]
    public void ValidateLengthInBeats_LengthLongerThanExpected_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G"], timeSignatureBeatCount: 3);
        CandidateTrack candidateTrack = CreateCandidateTrack(lengthInBeats: 7);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateLengthInBeats(candidateTrack, generationJob);

        Assert.Single(failures);
    }

    [Fact]
    public void ValidateDrumNoteChoices_AllNotesWithinGeneralMidiDrumRange_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        DrumNoteEvent[] drumNoteEvents =
        [
            new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 35, Velocity: 100),
            new(BeatPosition: 1, DurationInBeats: 1, MidiNote: 81, Velocity: 100),
            new(BeatPosition: 2, DurationInBeats: 1, MidiNote: 42, Velocity: 100),
        ];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateDrumNoteChoices(drumNoteEvents);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateDrumNoteChoices_NoteBelowGeneralMidiDrumRange_ReturnsFailureNamingNoteAndBeatPosition()
    {
        TrackValidator validator = CreateTrackValidator();
        DrumNoteEvent[] drumNoteEvents = [new(BeatPosition: 2.5, DurationInBeats: 1, MidiNote: 34, Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateDrumNoteChoices(drumNoteEvents);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Contains("34", failure.Reason);
        Assert.Contains("2.5", failure.Reason);
    }

    [Fact]
    public void ValidateDrumNoteChoices_NoteAboveGeneralMidiDrumRange_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        DrumNoteEvent[] drumNoteEvents = [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 82, Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateDrumNoteChoices(drumNoteEvents);

        Assert.Single(failures);
    }

    [Fact]
    public void ValidateDrumNoteChoices_MultipleNotesOutOfRange_ReturnsOneFailurePerOutOfRangeNote()
    {
        TrackValidator validator = CreateTrackValidator();
        DrumNoteEvent[] drumNoteEvents =
        [
            new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 10, Velocity: 100),
            new(BeatPosition: 1, DurationInBeats: 1, MidiNote: 50, Velocity: 100),
            new(BeatPosition: 2, DurationInBeats: 1, MidiNote: 120, Velocity: 100),
        ];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateDrumNoteChoices(drumNoteEvents);

        Assert.Equal(2, failures.Count);
    }

    private static readonly ChordEntry MajChordEntry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" };
    private static readonly ChordEntry MinChordEntry = new() { Id = "min", Intervals = [0, 3, 7], Description = "Minor triad" };
    private static readonly ScaleEntry MajorScaleEntry = new() { Id = "major", Intervals = [0, 2, 4, 5, 7, 9, 11], Description = "Major scale" };

    [Fact]
    public void ValidateBassNoteChoices_NotesAreChordTonesForBarsChord_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        MatchedChord gMajor = new(MajChordEntry, "G");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor, gMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        BassNoteEvent[] bassNoteEvents =
        [
            new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 36, Velocity: 100), // C in bar 1
            new(BeatPosition: 4, DurationInBeats: 1, MidiNote: 43, Velocity: 100), // G in bar 2
        ];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBassNoteChoices(bassNoteEvents, matchedChords, matchedScales, generationJob);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateBassNoteChoices_NoteIsScaleToneButNotChordTone_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [new(MajorScaleEntry, cMajor)];

        // D (pitch class 2) is in the C major scale but not a C major chord tone.
        BassNoteEvent[] bassNoteEvents = [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 38, Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBassNoteChoices(bassNoteEvents, matchedChords, matchedScales, generationJob);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateBassNoteChoices_NoteOutsideChordAndScaleTones_ReturnsFailureNamingBarAndNote()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [new(MajorScaleEntry, cMajor)];

        // C# (pitch class 1) is in neither the C major chord nor the C major scale.
        BassNoteEvent[] bassNoteEvents = [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 37, Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBassNoteChoices(bassNoteEvents, matchedChords, matchedScales, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Contains("bar 1", failure.Reason);
        Assert.Contains("37", failure.Reason);
    }

    [Fact]
    public void ValidateBassNoteChoices_NoteValidForWrongBarsChord_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        MatchedChord gMajor = new(MajChordEntry, "G");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor, gMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        // C (pitch class 0) is a chord tone in bar 1 but not bar 2's G major chord, and falls in bar 2.
        BassNoteEvent[] bassNoteEvents = [new(BeatPosition: 4, DurationInBeats: 1, MidiNote: 36, Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBassNoteChoices(bassNoteEvents, matchedChords, matchedScales, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Contains("bar 2", failure.Reason);
    }

    [Fact]
    public void ValidateBassNoteChoices_NoteBelowBassMidiRange_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        BassNoteEvent[] bassNoteEvents = [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 24, Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBassNoteChoices(bassNoteEvents, matchedChords, matchedScales, generationJob);

        Assert.Single(failures);
    }

    [Fact]
    public void ValidateBassNoteChoices_NoteAboveBassMidiRange_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        BassNoteEvent[] bassNoteEvents = [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 60, Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBassNoteChoices(bassNoteEvents, matchedChords, matchedScales, generationJob);

        Assert.Single(failures);
    }

    [Fact]
    public void ValidateBassNoteChoices_MinorChordTone_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["Am"], timeSignatureBeatCount: 4);

        MatchedChord aMinor = new(MinChordEntry, "A");
        IReadOnlyList<MatchedChord> matchedChords = [aMinor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        // C (pitch class 0) is the minor third of A minor.
        BassNoteEvent[] bassNoteEvents = [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 36, Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBassNoteChoices(bassNoteEvents, matchedChords, matchedScales, generationJob);

        Assert.Empty(failures);
    }
}
