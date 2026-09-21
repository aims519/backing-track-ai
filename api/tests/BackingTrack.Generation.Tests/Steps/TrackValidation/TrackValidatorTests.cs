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
        KeyMode = KeyMode.Ionian,
        KeyRoot = "C",
        Style = "rock",
        TimeSignatureBeatCount = timeSignatureBeatCount,
        TimeSignatureBeatUnit = 4,
        FeedbackItems = []
    };

    private static CandidateTrack CreateCandidateTrack(double lengthInBeats,
        IReadOnlyList<DrumNoteEvent>? drums = null,
        IReadOnlyList<BassNoteEvent>? bass = null,
        IReadOnlyList<ChordVoicingEvent>? chordVoicings = null) =>
        new(lengthInBeats, drums ?? [], bass ?? [], chordVoicings ?? []);

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
        Assert.Equal(ValidationFailureType.InvalidTrackLengthInBeats, failure.Type);
        Assert.Null(failure.NoteEvent);
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

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.InvalidTrackLengthInBeats, failure.Type);
        Assert.Null(failure.NoteEvent);
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
        Assert.Equal(ValidationFailureType.InvalidDrumNoteChoice, failure.Type);
        Assert.Same(drumNoteEvents[0], failure.NoteEvent);
        Assert.Contains("34", failure.Reason);
        Assert.Contains("2.5", failure.Reason);
    }

    [Fact]
    public void ValidateDrumNoteChoices_NoteAboveGeneralMidiDrumRange_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        DrumNoteEvent[] drumNoteEvents = [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 82, Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateDrumNoteChoices(drumNoteEvents);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.InvalidDrumNoteChoice, failure.Type);
        Assert.Same(drumNoteEvents[0], failure.NoteEvent);
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
        Assert.All(failures, f => Assert.Equal(ValidationFailureType.InvalidDrumNoteChoice, f.Type));
        Assert.Same(drumNoteEvents[0], failures[0].NoteEvent);
        Assert.Same(drumNoteEvents[2], failures[1].NoteEvent);
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
        Assert.Equal(ValidationFailureType.InvalidBassNoteChoice, failure.Type);
        Assert.Same(bassNoteEvents[0], failure.NoteEvent);
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
        Assert.Equal(ValidationFailureType.InvalidBassNoteChoice, failure.Type);
        Assert.Same(bassNoteEvents[0], failure.NoteEvent);
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

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.BassNoteOutOfExpectedRange, failure.Type);
        Assert.Same(bassNoteEvents[0], failure.NoteEvent);
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

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.BassNoteOutOfExpectedRange, failure.Type);
        Assert.Same(bassNoteEvents[0], failure.NoteEvent);
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

    [Fact]
    public void ValidateChordVoicingNoteChoices_AllNotesAreChordTonesForBarsChord_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        MatchedChord gMajor = new(MajChordEntry, "G");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor, gMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        ChordVoicingEvent[] chordVoicingEvents =
        [
            new(BeatPosition: 0, DurationInBeats: 4, MidiNotes: [48, 52, 55], Velocity: 100), // C major triad in bar 1
            new(BeatPosition: 4, DurationInBeats: 4, MidiNotes: [55, 59, 62], Velocity: 100), // G major triad in bar 2
        ];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices(chordVoicingEvents, matchedChords, matchedScales, generationJob);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateChordVoicingNoteChoices_NoteIsScaleToneButNotChordTone_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [new(MajorScaleEntry, cMajor)];

        // D (pitch class 2) is in the C major scale but not a C major chord tone.
        ChordVoicingEvent[] chordVoicingEvents = [new(BeatPosition: 0, DurationInBeats: 4, MidiNotes: [48, 50, 55], Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices(chordVoicingEvents, matchedChords, matchedScales, generationJob);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateChordVoicingNoteChoices_NoteOutsideChordAndScaleTones_ReturnsFailureNamingBarAndNote()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [new(MajorScaleEntry, cMajor)];

        // C# (pitch class 1) is in neither the C major chord nor the C major scale.
        ChordVoicingEvent[] chordVoicingEvents = [new(BeatPosition: 0, DurationInBeats: 4, MidiNotes: [48, 49, 55], Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices(chordVoicingEvents, matchedChords, matchedScales, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.InvalidChordVoicingNoteChoice, failure.Type);
        Assert.Same(chordVoicingEvents[0], failure.NoteEvent);
        Assert.Contains("bar 1", failure.Reason);
        Assert.Contains("49", failure.Reason);
    }

    [Fact]
    public void ValidateChordVoicingNoteChoices_MultipleInvalidNotesInSameVoicing_ReturnsOneFailurePerInvalidNote()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        // C# and D# (pitch classes 1 and 3) are both outside the C major chord tones.
        ChordVoicingEvent[] chordVoicingEvents = [new(BeatPosition: 0, DurationInBeats: 4, MidiNotes: [48, 49, 51], Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices(chordVoicingEvents, matchedChords, matchedScales, generationJob);

        Assert.Equal(2, failures.Count);
        Assert.All(failures, f => Assert.Equal(ValidationFailureType.InvalidChordVoicingNoteChoice, f.Type));
        Assert.All(failures, f => Assert.Same(chordVoicingEvents[0], f.NoteEvent));
    }

    [Fact]
    public void ValidateChordVoicingNoteChoices_NoteValidForWrongBarsChord_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        MatchedChord gMajor = new(MajChordEntry, "G");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor, gMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        // C major triad is valid for bar 1's chord but falls in bar 2 (G major).
        ChordVoicingEvent[] chordVoicingEvents = [new(BeatPosition: 4, DurationInBeats: 4, MidiNotes: [48, 52, 55], Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices(chordVoicingEvents, matchedChords, matchedScales, generationJob);

        Assert.NotEmpty(failures);
        Assert.All(failures, f => Assert.Contains("bar 2", f.Reason));
        Assert.All(failures, f => Assert.Equal(ValidationFailureType.InvalidChordVoicingNoteChoice, f.Type));
        Assert.All(failures, f => Assert.Same(chordVoicingEvents[0], f.NoteEvent));
    }

    [Fact]
    public void ValidateChordVoicingNoteChoices_MinorChordTones_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["Am"], timeSignatureBeatCount: 4);

        MatchedChord aMinor = new(MinChordEntry, "A");
        IReadOnlyList<MatchedChord> matchedChords = [aMinor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        // A, C, E - the A minor triad.
        ChordVoicingEvent[] chordVoicingEvents = [new(BeatPosition: 0, DurationInBeats: 4, MidiNotes: [57, 60, 64], Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices(chordVoicingEvents, matchedChords, matchedScales, generationJob);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateChordVoicingNoteChoices_NoVoicingsInBar_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices([], matchedChords, matchedScales, generationJob);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateChordVoicingNoteChoices_AllNotesWithinChordVoicingRange_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        // 48 and 79 are the inclusive range boundaries (C3 and G5).
        ChordVoicingEvent[] chordVoicingEvents = [new(BeatPosition: 0, DurationInBeats: 4, MidiNotes: [48, 60, 79], Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices(chordVoicingEvents, matchedChords, matchedScales, generationJob);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateChordVoicingNoteChoices_NoteBelowChordVoicingRange_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        // C (a chord tone) an octave below the range floor.
        ChordVoicingEvent[] chordVoicingEvents = [new(BeatPosition: 0, DurationInBeats: 4, MidiNotes: [36], Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices(chordVoicingEvents, matchedChords, matchedScales, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.ChordVoicingNoteOutOfExpectedRange, failure.Type);
        Assert.Same(chordVoicingEvents[0], failure.NoteEvent);
        Assert.Contains("36", failure.Reason);
    }

    [Fact]
    public void ValidateChordVoicingNoteChoices_NoteAboveChordVoicingRange_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        // C (a chord tone) an octave above the range ceiling.
        ChordVoicingEvent[] chordVoicingEvents = [new(BeatPosition: 0, DurationInBeats: 4, MidiNotes: [84], Velocity: 100)];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices(chordVoicingEvents, matchedChords, matchedScales, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.ChordVoicingNoteOutOfExpectedRange, failure.Type);
        Assert.Same(chordVoicingEvents[0], failure.NoteEvent);
        Assert.Contains("84", failure.Reason);
    }

    [Fact]
    public void ValidateChordVoicingNoteChoices_MultipleNotesOutOfRangeAcrossVoicings_ReturnsOneFailurePerOutOfRangeNote()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C"], timeSignatureBeatCount: 4);

        MatchedChord cMajor = new(MajChordEntry, "C");
        IReadOnlyList<MatchedChord> matchedChords = [cMajor];
        IReadOnlyList<MatchedScale> matchedScales = [];

        ChordVoicingEvent[] chordVoicingEvents =
        [
            new(BeatPosition: 0, DurationInBeats: 2, MidiNotes: [24, 60], Velocity: 100), // 24 (C) below range, 60 (C) in range
            new(BeatPosition: 2, DurationInBeats: 2, MidiNotes: [96], Velocity: 100), // 96 (C) above range
        ];

        IReadOnlyList<ValidationFailure> failures = validator.ValidateChordVoicingNoteChoices(chordVoicingEvents, matchedChords, matchedScales, generationJob);

        Assert.Equal(2, failures.Count);
        Assert.All(failures, f => Assert.Equal(ValidationFailureType.ChordVoicingNoteOutOfExpectedRange, f.Type));
        Assert.Same(chordVoicingEvents[0], failures[0].NoteEvent);
        Assert.Same(chordVoicingEvents[1], failures[1].NoteEvent);
    }

    [Fact]
    public void ValidateMidiVelocities_AllVelocitiesWithinRange_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 1)],
            bass: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 127)],
            chordVoicings: [new(BeatPosition: 0, DurationInBeats: 1, MidiNotes: [60], Velocity: 64)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateMidiVelocities(candidateTrack);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateMidiVelocities_DrumVelocityBelowRange_ReturnsFailureNamingVelocity()
    {
        TrackValidator validator = CreateTrackValidator();
        DrumNoteEvent drumNoteEvent = new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 0);
        CandidateTrack candidateTrack = CreateCandidateTrack(16, drums: [drumNoteEvent]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateMidiVelocities(candidateTrack);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.InvalidMidiVelocity, failure.Type);
        Assert.Same(drumNoteEvent, failure.NoteEvent);
        Assert.Contains(nameof(DrumNoteEvent), failure.Reason);
        Assert.Contains("0", failure.Reason);
    }

    [Fact]
    public void ValidateMidiVelocities_BassVelocityAboveRange_ReturnsFailureNamingVelocity()
    {
        TrackValidator validator = CreateTrackValidator();
        BassNoteEvent bassNoteEvent = new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 128);
        CandidateTrack candidateTrack = CreateCandidateTrack(16, bass: [bassNoteEvent]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateMidiVelocities(candidateTrack);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.InvalidMidiVelocity, failure.Type);
        Assert.Same(bassNoteEvent, failure.NoteEvent);
        Assert.Contains(nameof(BassNoteEvent), failure.Reason);
        Assert.Contains("128", failure.Reason);
    }

    [Fact]
    public void ValidateMidiVelocities_ChordVoicingVelocityOutOfRange_ReturnsFailureNamingVelocity()
    {
        TrackValidator validator = CreateTrackValidator();
        ChordVoicingEvent chordVoicingEvent = new(BeatPosition: 0, DurationInBeats: 1, MidiNotes: [60], Velocity: -1);
        CandidateTrack candidateTrack = CreateCandidateTrack(16, chordVoicings: [chordVoicingEvent]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateMidiVelocities(candidateTrack);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.InvalidMidiVelocity, failure.Type);
        Assert.Same(chordVoicingEvent, failure.NoteEvent);
        Assert.Contains(nameof(ChordVoicingEvent), failure.Reason);
    }

    [Fact]
    public void ValidateMidiVelocities_MultiplePartsOutOfRange_ReturnsOneFailurePerPart()
    {
        TrackValidator validator = CreateTrackValidator();
        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 0)],
            bass: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 200)],
            chordVoicings: [new(BeatPosition: 0, DurationInBeats: 1, MidiNotes: [60], Velocity: -5)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateMidiVelocities(candidateTrack);

        Assert.Equal(3, failures.Count);
        Assert.All(failures, f => Assert.Equal(ValidationFailureType.InvalidMidiVelocity, f.Type));
        Assert.All(failures, f => Assert.NotNull(f.NoteEvent));
    }

    [Fact]
    public void ValidateNoPartEmpty_AllPartsHaveEvents_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 100)],
            bass: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 100)],
            chordVoicings: [new(BeatPosition: 0, DurationInBeats: 1, MidiNotes: [60], Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateNoPartEmpty(candidateTrack);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateNoPartEmpty_DrumsEmpty_ReturnsFailureNamingDrumNoteEvent()
    {
        TrackValidator validator = CreateTrackValidator();
        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            bass: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 100)],
            chordVoicings: [new(BeatPosition: 0, DurationInBeats: 1, MidiNotes: [60], Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateNoPartEmpty(candidateTrack);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.PartEmpty, failure.Type);
        Assert.Null(failure.NoteEvent);
        Assert.Contains(nameof(DrumNoteEvent), failure.Reason);
    }

    [Fact]
    public void ValidateNoPartEmpty_BassEmpty_ReturnsFailureNamingBassNoteEvent()
    {
        TrackValidator validator = CreateTrackValidator();
        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 100)],
            chordVoicings: [new(BeatPosition: 0, DurationInBeats: 1, MidiNotes: [60], Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateNoPartEmpty(candidateTrack);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.PartEmpty, failure.Type);
        Assert.Null(failure.NoteEvent);
        Assert.Contains(nameof(BassNoteEvent), failure.Reason);
    }

    [Fact]
    public void ValidateNoPartEmpty_ChordVoicingsEmpty_ReturnsFailureNamingChordVoicingEvent()
    {
        TrackValidator validator = CreateTrackValidator();
        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 100)],
            bass: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateNoPartEmpty(candidateTrack);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.PartEmpty, failure.Type);
        Assert.Null(failure.NoteEvent);
        Assert.Contains(nameof(ChordVoicingEvent), failure.Reason);
    }

    [Fact]
    public void ValidateNoPartEmpty_AllPartsEmpty_ReturnsOneFailurePerPart()
    {
        TrackValidator validator = CreateTrackValidator();
        CandidateTrack candidateTrack = CreateCandidateTrack(16);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateNoPartEmpty(candidateTrack);

        Assert.Equal(3, failures.Count);
        Assert.All(failures, f => Assert.Equal(ValidationFailureType.PartEmpty, f.Type));
        Assert.All(failures, f => Assert.Null(f.NoteEvent));
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_AllEventsWithinBoundsWithPositiveDurations_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4); // maxBeatPosition = 16

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: 0, DurationInBeats: 1, MidiNote: 40, Velocity: 100)],
            bass: [new(BeatPosition: 4, DurationInBeats: 2, MidiNote: 40, Velocity: 100)],
            chordVoicings: [new(BeatPosition: 12, DurationInBeats: 4, MidiNotes: [60], Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_DrumNoteBeatPositionNegative_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: -1, DurationInBeats: 1, MidiNote: 40, Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.BeatPositionOutOfRange, failure.Type);
        Assert.Same(candidateTrack.Drums[0], failure.NoteEvent);
        Assert.Contains(nameof(DrumNoteEvent.BeatPosition), failure.Reason);
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_DrumNoteBeatPositionBeyondTrackEnd_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4); // maxBeatPosition = 16

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: 17, DurationInBeats: 1, MidiNote: 40, Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        // A note starting beyond the track end also fails the "extends beyond track end" check,
        // so this raises two failures rather than one.
        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, f => f.Type == ValidationFailureType.BeatPositionOutOfRange);
        Assert.Contains(failures, f => f.Type == ValidationFailureType.NoteBeyondTrackEnd);
        Assert.All(failures, f => Assert.Same(candidateTrack.Drums[0], f.NoteEvent));
        Assert.Contains(failures, f => f.Reason.Contains(nameof(DrumNoteEvent.BeatPosition)) && !f.Reason.Contains(nameof(DrumNoteEvent.DurationInBeats)));
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_DrumNoteNegativeDuration_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: 0, DurationInBeats: -1, MidiNote: 40, Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.InvalidNoteDuration, failure.Type);
        Assert.Same(candidateTrack.Drums[0], failure.NoteEvent);
        Assert.Contains(nameof(DrumNoteEvent.DurationInBeats), failure.Reason);
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_DrumNoteExtendsBeyondTrackEnd_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4); // maxBeatPosition = 16

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: 15, DurationInBeats: 4, MidiNote: 40, Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.NoteBeyondTrackEnd, failure.Type);
        Assert.Same(candidateTrack.Drums[0], failure.NoteEvent);
        Assert.Contains(nameof(DrumNoteEvent.BeatPosition), failure.Reason);
        Assert.Contains(nameof(DrumNoteEvent.DurationInBeats), failure.Reason);
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_BassNoteBeatPositionNegative_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            bass: [new(BeatPosition: -1, DurationInBeats: 1, MidiNote: 40, Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.BeatPositionOutOfRange, failure.Type);
        Assert.Same(candidateTrack.Bass[0], failure.NoteEvent);
        Assert.Contains(nameof(BassNoteEvent.BeatPosition), failure.Reason);
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_BassNoteExtendsBeyondTrackEnd_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4); // maxBeatPosition = 16

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            bass: [new(BeatPosition: 15, DurationInBeats: 4, MidiNote: 40, Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.NoteBeyondTrackEnd, failure.Type);
        Assert.Same(candidateTrack.Bass[0], failure.NoteEvent);
        Assert.Contains(nameof(BassNoteEvent.DurationInBeats), failure.Reason);
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_BassNoteNegativeDuration_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            bass: [new(BeatPosition: 0, DurationInBeats: -1, MidiNote: 40, Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.InvalidNoteDuration, failure.Type);
        Assert.Same(candidateTrack.Bass[0], failure.NoteEvent);
        Assert.Contains(nameof(BassNoteEvent.DurationInBeats), failure.Reason);
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_ChordVoicingBeatPositionNegative_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            chordVoicings: [new(BeatPosition: -1, DurationInBeats: 1, MidiNotes: [60], Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.BeatPositionOutOfRange, failure.Type);
        Assert.Same(candidateTrack.ChordVoicings[0], failure.NoteEvent);
        Assert.Contains(nameof(ChordVoicingEvent.BeatPosition), failure.Reason);
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_ChordVoicingExtendsBeyondTrackEnd_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4); // maxBeatPosition = 16

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            chordVoicings: [new(BeatPosition: 15, DurationInBeats: 4, MidiNotes: [60], Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.NoteBeyondTrackEnd, failure.Type);
        Assert.Same(candidateTrack.ChordVoicings[0], failure.NoteEvent);
        Assert.Contains(nameof(ChordVoicingEvent.DurationInBeats), failure.Reason);
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_ChordVoicingNegativeDuration_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            chordVoicings: [new(BeatPosition: 0, DurationInBeats: -1, MidiNotes: [60], Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.InvalidNoteDuration, failure.Type);
        Assert.Same(candidateTrack.ChordVoicings[0], failure.NoteEvent);
        Assert.Contains(nameof(ChordVoicingEvent.DurationInBeats), failure.Reason);
    }

    [Fact]
    public void ValidateBeatPositionsAndDurations_ZeroDuration_ReturnsFailure()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(BeatPosition: 0, DurationInBeats: 0, MidiNote: 40, Velocity: 100)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.InvalidNoteDuration, failure.Type);
        Assert.Same(candidateTrack.Drums[0], failure.NoteEvent);
        Assert.Contains(nameof(DrumNoteEvent.DurationInBeats), failure.Reason);
    }

    [Fact]
    public void ValidateSilentBars_EveryBarHasAllThreeParts_ReturnsNoFailures()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(0, 1, 42, 100), new(4, 1, 42, 100), new(8, 1, 42, 100), new(12, 1, 42, 100)],
            bass: [new(0, 1, 36, 100), new(4, 1, 43, 100), new(8, 1, 45, 100), new(12, 1, 41, 100)],
            chordVoicings:
            [
                new(0, 4, [60, 64, 67], 90), new(4, 4, [62, 65, 69], 90),
                new(8, 4, [60, 64, 69], 90), new(12, 4, [60, 65, 69], 90)
            ]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateSilentBars(candidateTrack, generationJob);

        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateSilentBars_FinalBarHasNoEvents_ReturnsOneFailurePerPartForThatBar()
    {
        // The dropped-final-bar case: LengthInBeats is fixed to 16 in code, so a track that only
        // fills 3 of 4 bars would otherwise slip through validation with a silent bar 4.
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am", "F"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(16,
            drums: [new(0, 1, 42, 100), new(4, 1, 42, 100), new(8, 1, 42, 100)],
            bass: [new(0, 1, 36, 100), new(4, 1, 43, 100), new(8, 1, 45, 100)],
            chordVoicings: [new(0, 4, [60, 64, 67], 90), new(4, 4, [62, 65, 69], 90), new(8, 4, [60, 64, 69], 90)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateSilentBars(candidateTrack, generationJob);

        Assert.Equal(3, failures.Count);
        Assert.All(failures, f => Assert.Equal(ValidationFailureType.SilentBar, f.Type));
        Assert.All(failures, f => Assert.Null(f.NoteEvent));
        Assert.All(failures, f => Assert.Contains("bar 4", f.Reason));
        Assert.Contains(failures, f => f.Reason.Contains(nameof(DrumNoteEvent), StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Reason.Contains(nameof(BassNoteEvent), StringComparison.Ordinal));
        Assert.Contains(failures, f => f.Reason.Contains(nameof(ChordVoicingEvent), StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateSilentBars_BarMissingOnlyBass_ReturnsSingleFailureNamingBassAndBar()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(8,
            drums: [new(0, 1, 42, 100), new(4, 1, 42, 100)],
            bass: [new(0, 1, 36, 100)],
            chordVoicings: [new(0, 4, [60, 64, 67], 90), new(4, 4, [62, 65, 69], 90)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateSilentBars(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.SilentBar, failure.Type);
        Assert.Contains(nameof(BassNoteEvent), failure.Reason);
        Assert.Contains("bar 2", failure.Reason);
    }

    [Fact]
    public void ValidateSilentBars_BarMissingOnlyChordVoicings_ReturnsSingleFailureNamingChordVoicingAndBar()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(8,
            drums: [new(0, 1, 42, 100), new(4, 1, 42, 100)],
            bass: [new(0, 1, 36, 100), new(4, 1, 43, 100)],
            chordVoicings: [new(0, 4, [60, 64, 67], 90)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateSilentBars(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.SilentBar, failure.Type);
        Assert.Contains(nameof(ChordVoicingEvent), failure.Reason);
        Assert.Contains("bar 2", failure.Reason);
    }

    [Fact]
    public void ValidateSilentBars_BarMissingOnlyDrums_ReturnsSingleFailureNamingDrumAndBar()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(8,
            drums: [new(0, 1, 42, 100)],
            bass: [new(0, 1, 36, 100), new(4, 1, 43, 100)],
            chordVoicings: [new(0, 4, [60, 64, 67], 90), new(4, 4, [62, 65, 69], 90)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateSilentBars(candidateTrack, generationJob);

        ValidationFailure failure = Assert.Single(failures);
        Assert.Equal(ValidationFailureType.SilentBar, failure.Type);
        Assert.Contains(nameof(DrumNoteEvent), failure.Reason);
        Assert.Contains("bar 2", failure.Reason);
    }

    [Fact]
    public void ValidateSilentBars_NoteExactlyOnBarBoundary_CountsTowardsTheLaterBar()
    {
        // BeatPosition 4.0 in 4/4 is the downbeat of bar 2, so bar 1 has nothing.
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(8,
            drums: [new(4, 1, 42, 100)],
            bass: [new(4, 1, 36, 100)],
            chordVoicings: [new(4, 4, [60, 64, 67], 90)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateSilentBars(candidateTrack, generationJob);

        Assert.Equal(3, failures.Count);
        Assert.All(failures, f => Assert.Contains("bar 1", f.Reason));
    }

    [Fact]
    public void ValidateSilentBars_TrackWithNoEvents_ReturnsThreeFailuresPerBar()
    {
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G", "Am"], timeSignatureBeatCount: 4);

        CandidateTrack candidateTrack = CreateCandidateTrack(12);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateSilentBars(candidateTrack, generationJob);

        Assert.Equal(9, failures.Count);
        Assert.All(failures, f => Assert.Equal(ValidationFailureType.SilentBar, f.Type));
    }

    [Fact]
    public void ValidateSilentBars_UsesTimeSignatureBeatCountForBarWindows()
    {
        // 3/4: bar 1 = [0,3), bar 2 = [3,6). Every part has a note in each 3-beat window.
        TrackValidator validator = CreateTrackValidator();
        GenerationJob generationJob = CreateGenerationJob(chords: ["C", "G"], timeSignatureBeatCount: 3);

        CandidateTrack candidateTrack = CreateCandidateTrack(6,
            drums: [new(0, 1, 42, 100), new(3, 1, 42, 100)],
            bass: [new(0, 1, 36, 100), new(3, 1, 43, 100)],
            chordVoicings: [new(0, 3, [60, 64, 67], 90), new(3, 3, [62, 65, 69], 90)]);

        IReadOnlyList<ValidationFailure> failures = validator.ValidateSilentBars(candidateTrack, generationJob);

        Assert.Empty(failures);
    }
}