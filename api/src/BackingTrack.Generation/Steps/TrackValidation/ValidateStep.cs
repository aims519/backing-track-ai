using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Steps.TrackValidation;

public class ValidateStep(ITrackValidator trackValidator) : IValidateStep
{
    public TrackValidationResult ValidateCandidateTrack(CandidateTrack candidateTrack, GenerationJob generationJob)
    {
        List<ValidationFailure> failures =
        [
            .. trackValidator.ValidateLengthInBeats(candidateTrack, generationJob),
            ..trackValidator.ValidateDrumNoteChoices(candidateTrack.Drums)
        ];

        return new TrackValidationResult(failures.Count == 0, failures);
    }
}