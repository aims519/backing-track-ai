using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Eval.Running;

/// <summary>
/// Decorates the real <see cref="IGenerateStep"/> so a <c>Fallback</c> case can simulate an LLM
/// outage (the generate call throwing, as a timeout or malformed response would) and prove the
/// workflow takes the rule-based fallback instead of failing the job — ADR 0034. Unlike
/// <see cref="FaultInjectingValidateStep"/> and <see cref="FaultInjectingCriticStep"/> this fault
/// can't be reached with a "hard" input at all, so there is no real-model alternative to hope for.
/// Covers both generate entry points; repair and critic faults are unit-tested per executor.
/// </summary>
public sealed class FaultInjectingGenerateStep(GenerateStep innerGenerateStep, GenerateFaultInjectionState faultInjectionState) : IGenerateStep
{
    public Task<CandidateTrack> GenerateCandidateTrackAsync(string keyRoot, KeyMode keyMode, int tempo,
        int timeSignatureBeatCount, int timeSignatureBeatUnit, MatchedStyle style,
        IReadOnlyList<MatchedChord> chords, IReadOnlyList<MatchedScale> scales,
        CandidateTrack? previousAttempt = null, IReadOnlyList<ValidationFailure>? previousAttemptFailures = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfForced();

        return innerGenerateStep.GenerateCandidateTrackAsync(keyRoot, keyMode, tempo, timeSignatureBeatCount,
            timeSignatureBeatUnit, style, chords, scales, previousAttempt, previousAttemptFailures, cancellationToken);
    }

    public Task<CandidateTrack> GenerateCandidateTrackWithCriticReviewAsync(string keyRoot, KeyMode keyMode, int tempo,
        int timeSignatureBeatCount, int timeSignatureBeatUnit, MatchedStyle style,
        IReadOnlyList<MatchedChord> chords, IReadOnlyList<MatchedScale> scales,
        CandidateTrack previousAttempt, CriticFinalReview criticReview,
        CancellationToken cancellationToken = default)
    {
        ThrowIfForced();

        return innerGenerateStep.GenerateCandidateTrackWithCriticReviewAsync(keyRoot, keyMode, tempo, timeSignatureBeatCount,
            timeSignatureBeatUnit, style, chords, scales, previousAttempt, criticReview, cancellationToken);
    }

    private void ThrowIfForced()
    {
        if (faultInjectionState.ExceptionsToForce <= 0)
            return;

        faultInjectionState.ExceptionsToForce--;
        throw new HttpRequestException("Eval harness fault injection — simulated LLM outage, not a real generation failure");
    }
}