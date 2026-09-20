using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Eval.Running;

/// <summary>
/// Decorates the real <see cref="IValidateStep"/> so a <c>Fallback</c> case can guarantee the
/// validator's own repair-budget exhaustion → fallback branch runs, instead of relying on a
/// "hard" input to genuinely exhaust the repair loop — empirically that was unreliable, the same
/// problem <see cref="FaultInjectingCriticStep"/> solves for the critic's exhaustion branch.
/// Forcing exactly as many failures as the fallback track's own (real) validation call would
/// still succeed: this only ever fails the pre-fallback candidates, never the fallback track
/// itself, because the count runs out before that call happens.
/// </summary>
public sealed class FaultInjectingValidateStep(ValidateStep innerValidateStep, ValidationFaultInjectionState faultInjectionState) : IValidateStep
{
    public TrackValidationResult ValidateCandidateTrack(CandidateTrack candidateTrack, GenerationJob generationJob,
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales)
    {
        if (faultInjectionState.FailuresToForce > 0)
        {
            faultInjectionState.FailuresToForce--;
            ValidationFailure forcedFailure = new(
                ValidationFailureType.InvalidNoteDuration,
                NoteEvent: null,
                Reason: "Eval harness fault injection — not a real validation finding");
            return new TrackValidationResult(IsValid: false, [forcedFailure]);
        }

        return innerValidateStep.ValidateCandidateTrack(candidateTrack, generationJob, matchedChords, matchedScales);
    }
}