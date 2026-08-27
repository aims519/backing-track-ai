using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

using NSubstitute;

namespace BackingTrack.Generation.Tests.Steps.TrackValidation;

public class ValidateStepTests
{
    private static GenerationJob CreateGenerationJob() => new()
    {
        Id = Guid.NewGuid(),
        State = JobStates.Pending,
        StartTime = DateTimeOffset.UtcNow,
        Mode = GenerationMode.Precise,
        Tempo = 120,
        Chords = ["C", "G", "Am", "F"],
        KeyRoot = "C",
        Style = "rock",
        TimeSignatureBeatCount = 4,
        TimeSignatureBeatUnit = 4,
    };

    private static CandidateTrack CreateCandidateTrack() => new(16, [], [], []);

    private static ITrackValidator CreateTrackValidatorMockWithNoFailures()
    {
        ITrackValidator mock = Substitute.For<ITrackValidator>();

        mock.ValidateLengthInBeats(Arg.Any<CandidateTrack>(), Arg.Any<GenerationJob>()).Returns([]);

        mock.ValidateDrumNoteChoices(Arg.Any<IReadOnlyList<DrumNoteEvent>>()).Returns([]);

        mock.ValidateBassNoteChoices(Arg.Any<IReadOnlyList<BassNoteEvent>>(), Arg.Any<IReadOnlyList<MatchedChord>>(),
                Arg.Any<IReadOnlyList<MatchedScale>>(), Arg.Any<GenerationJob>()).Returns([]);

        mock.ValidateChordVoicingNoteChoices(Arg.Any<IReadOnlyList<ChordVoicingEvent>>(), Arg.Any<IReadOnlyList<MatchedChord>>(),
            Arg.Any<IReadOnlyList<MatchedScale>>(), Arg.Any<GenerationJob>()).Returns([]);

        mock.ValidateMidiVelocities(Arg.Any<CandidateTrack>()).Returns([]);

        mock.ValidateNoPartEmpty(Arg.Any<CandidateTrack>()).Returns([]);

        mock.ValidateBeatPositionsAndDurations(Arg.Any<CandidateTrack>(), Arg.Any<GenerationJob>()).Returns([]);

        return mock;
    }

    [Fact]
    public void ValidateCandidateTrack_AllChecksReturnNoFailures_ReturnsValidResultWithNoFailures()
    {
        ITrackValidator trackValidatorMock = CreateTrackValidatorMockWithNoFailures();
        ValidateStep validateStep = new(trackValidatorMock);

        TrackValidationResult result = validateStep.ValidateCandidateTrack(CreateCandidateTrack(), CreateGenerationJob(), [], []);

        Assert.True(result.IsValid);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void ValidateCandidateTrack_LengthCheckReturnsFailure_ReturnsInvalidResultContainingThatFailure()
    {
        ITrackValidator trackValidatorMock = CreateTrackValidatorMockWithNoFailures();
        ValidationFailure lengthFailure = new(ValidationFailureType.InvalidTrackLengthInBeats, null, "Length failure");
        trackValidatorMock.ValidateLengthInBeats(Arg.Any<CandidateTrack>(), Arg.Any<GenerationJob>()).Returns([lengthFailure]);

        ValidateStep validateStep = new(trackValidatorMock);

        TrackValidationResult result = validateStep.ValidateCandidateTrack(CreateCandidateTrack(), CreateGenerationJob(), [], []);

        Assert.False(result.IsValid);
        Assert.Contains(lengthFailure, result.Failures);
    }

    [Fact]
    public void ValidateCandidateTrack_MultipleChecksReturnFailures_ReturnsInvalidResultContainingAllFailures()
    {
        ITrackValidator trackValidatorMock = CreateTrackValidatorMockWithNoFailures();
        ValidationFailure drumFailure = new(ValidationFailureType.InvalidDrumNoteChoice, null, "Drum failure");
        ValidationFailure velocityFailure = new(ValidationFailureType.InvalidMidiVelocity, null, "Velocity failure");
        trackValidatorMock.ValidateDrumNoteChoices(Arg.Any<IReadOnlyList<DrumNoteEvent>>()).Returns([drumFailure]);
        trackValidatorMock.ValidateMidiVelocities(Arg.Any<CandidateTrack>()).Returns([velocityFailure]);

        ValidateStep validateStep = new(trackValidatorMock);

        TrackValidationResult result = validateStep.ValidateCandidateTrack(CreateCandidateTrack(), CreateGenerationJob(), [], []);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Failures.Count);
        Assert.Contains(drumFailure, result.Failures);
        Assert.Contains(velocityFailure, result.Failures);
    }

    [Fact]
    public void ValidateCandidateTrack_PassesCandidateTrackAndGenerationJobThroughToEachCheck()
    {
        ITrackValidator trackValidatorMock = CreateTrackValidatorMockWithNoFailures();
        ValidateStep validateStep = new(trackValidatorMock);

        CandidateTrack candidateTrack = CreateCandidateTrack();
        GenerationJob generationJob = CreateGenerationJob();
        IReadOnlyList<MatchedChord> matchedChords = [];
        IReadOnlyList<MatchedScale> matchedScales = [];

        validateStep.ValidateCandidateTrack(candidateTrack, generationJob, matchedChords, matchedScales);

        trackValidatorMock.Received(1).ValidateLengthInBeats(candidateTrack, generationJob);
        trackValidatorMock.Received(1).ValidateDrumNoteChoices(candidateTrack.Drums);
        trackValidatorMock.Received(1)
            .ValidateBassNoteChoices(candidateTrack.Bass, matchedChords, matchedScales, generationJob);
        trackValidatorMock.Received(1).ValidateChordVoicingNoteChoices(candidateTrack.ChordVoicings, matchedChords,
            matchedScales, generationJob);
        trackValidatorMock.Received(1).ValidateMidiVelocities(candidateTrack);
        trackValidatorMock.Received(1).ValidateNoPartEmpty(candidateTrack);
        trackValidatorMock.Received(1).ValidateBeatPositionsAndDurations(candidateTrack, generationJob);
    }
}