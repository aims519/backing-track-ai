using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackValidation;

public class ValidateStep(ITrackValidator trackValidator) : IValidateStep
{
    public TrackValidationResult ValidateCandidateTrack(CandidateTrack candidateTrack, GenerationJob generationJob, 
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales)
    {
        List<ValidationFailure> failures =
        [
            ..trackValidator.ValidateLengthInBeats(candidateTrack, generationJob),
            ..trackValidator.ValidateDrumNoteChoices(candidateTrack.Drums),
            ..trackValidator.ValidateBassNoteChoices(candidateTrack.Bass, matchedChords, matchedScales, generationJob),
            ..trackValidator.ValidateChordVoicingNoteChoices(candidateTrack.ChordVoicings,  matchedChords, matchedScales, generationJob),
            ..trackValidator.ValidateMidiVelocities(candidateTrack),
            ..trackValidator.ValidateNoPartEmpty(candidateTrack),
            ..trackValidator.ValidateBeatPositionsAndDurations(candidateTrack, generationJob)
        ];

        return new TrackValidationResult(failures.Count == 0, failures);
    }
}