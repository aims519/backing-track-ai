using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

using Moq;

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

    private static Mock<ITrackValidator> CreateTrackValidatorMockWithNoFailures()
    {
        Mock<ITrackValidator> mock = new();
        mock.Setup(v => v.ValidateLengthInBeats(It.IsAny<CandidateTrack>(), It.IsAny<GenerationJob>()))
            .Returns([]);
        mock.Setup(v => v.ValidateDrumNoteChoices(It.IsAny<IReadOnlyList<DrumNoteEvent>>()))
            .Returns([]);
        mock.Setup(v => v.ValidateBassNoteChoices(It.IsAny<IReadOnlyList<BassNoteEvent>>(), It.IsAny<IReadOnlyList<MatchedChord>>(),
                It.IsAny<IReadOnlyList<MatchedScale>>(), It.IsAny<GenerationJob>()))
            .Returns([]);
        mock.Setup(v => v.ValidateChordVoicingNoteChoices(It.IsAny<IReadOnlyList<ChordVoicingEvent>>(), It.IsAny<IReadOnlyList<MatchedChord>>(),
                It.IsAny<IReadOnlyList<MatchedScale>>(), It.IsAny<GenerationJob>()))
            .Returns([]);
        mock.Setup(v => v.ValidateMidiVelocities(It.IsAny<CandidateTrack>()))
            .Returns([]);
        mock.Setup(v => v.ValidateNoPartEmpty(It.IsAny<CandidateTrack>()))
            .Returns([]);
        mock.Setup(v => v.ValidateBeatPositionsAndDurations(It.IsAny<CandidateTrack>(), It.IsAny<GenerationJob>()))
            .Returns([]);

        return mock;
    }

    [Fact]
    public void ValidateCandidateTrack_AllChecksReturnNoFailures_ReturnsValidResultWithNoFailures()
    {
        Mock<ITrackValidator> trackValidatorMock = CreateTrackValidatorMockWithNoFailures();
        ValidateStep validateStep = new(trackValidatorMock.Object);

        TrackValidationResult result = validateStep.ValidateCandidateTrack(CreateCandidateTrack(), CreateGenerationJob(), [], []);

        Assert.True(result.IsValid);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void ValidateCandidateTrack_LengthCheckReturnsFailure_ReturnsInvalidResultContainingThatFailure()
    {
        Mock<ITrackValidator> trackValidatorMock = CreateTrackValidatorMockWithNoFailures();
        ValidationFailure lengthFailure = new("Length failure");
        trackValidatorMock.Setup(v => v.ValidateLengthInBeats(It.IsAny<CandidateTrack>(), It.IsAny<GenerationJob>()))
            .Returns([lengthFailure]);

        ValidateStep validateStep = new(trackValidatorMock.Object);

        TrackValidationResult result = validateStep.ValidateCandidateTrack(CreateCandidateTrack(), CreateGenerationJob(), [], []);

        Assert.False(result.IsValid);
        Assert.Contains(lengthFailure, result.Failures);
    }

    [Fact]
    public void ValidateCandidateTrack_MultipleChecksReturnFailures_ReturnsInvalidResultContainingAllFailures()
    {
        Mock<ITrackValidator> trackValidatorMock = CreateTrackValidatorMockWithNoFailures();
        ValidationFailure drumFailure = new("Drum failure");
        ValidationFailure velocityFailure = new("Velocity failure");
        trackValidatorMock.Setup(v => v.ValidateDrumNoteChoices(It.IsAny<IReadOnlyList<DrumNoteEvent>>()))
            .Returns([drumFailure]);
        trackValidatorMock.Setup(v => v.ValidateMidiVelocities(It.IsAny<CandidateTrack>()))
            .Returns([velocityFailure]);

        ValidateStep validateStep = new(trackValidatorMock.Object);

        TrackValidationResult result = validateStep.ValidateCandidateTrack(CreateCandidateTrack(), CreateGenerationJob(), [], []);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Failures.Count);
        Assert.Contains(drumFailure, result.Failures);
        Assert.Contains(velocityFailure, result.Failures);
    }

    [Fact]
    public void ValidateCandidateTrack_PassesCandidateTrackAndGenerationJobThroughToEachCheck()
    {
        Mock<ITrackValidator> trackValidatorMock = CreateTrackValidatorMockWithNoFailures();
        ValidateStep validateStep = new(trackValidatorMock.Object);

        CandidateTrack candidateTrack = CreateCandidateTrack();
        GenerationJob generationJob = CreateGenerationJob();
        IReadOnlyList<MatchedChord> matchedChords = [];
        IReadOnlyList<MatchedScale> matchedScales = [];

        validateStep.ValidateCandidateTrack(candidateTrack, generationJob, matchedChords, matchedScales);

        trackValidatorMock.Verify(v => v.ValidateLengthInBeats(candidateTrack, generationJob), Times.Once);
        trackValidatorMock.Verify(v => v.ValidateDrumNoteChoices(candidateTrack.Drums), Times.Once);
        trackValidatorMock.Verify(v => v.ValidateBassNoteChoices(candidateTrack.Bass, matchedChords, matchedScales, generationJob), Times.Once);
        trackValidatorMock.Verify(v => v.ValidateChordVoicingNoteChoices(candidateTrack.ChordVoicings, matchedChords, matchedScales, generationJob), Times.Once);
        trackValidatorMock.Verify(v => v.ValidateMidiVelocities(candidateTrack), Times.Once);
        trackValidatorMock.Verify(v => v.ValidateNoPartEmpty(candidateTrack), Times.Once);
        trackValidatorMock.Verify(v => v.ValidateBeatPositionsAndDurations(candidateTrack, generationJob), Times.Once);
    }
}
