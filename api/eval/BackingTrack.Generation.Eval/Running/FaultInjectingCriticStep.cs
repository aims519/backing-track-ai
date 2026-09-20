using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;

namespace BackingTrack.Generation.Eval.Running;

/// <summary>
/// Decorates the real <see cref="ICriticStep"/> so a <c>CriticTriggering</c> case can guarantee
/// the critic → regenerate loop runs, instead of relying on a "stylistically risky" input to
/// draw a genuine rejection from the model — empirically that was unreliable (and, separately,
/// the eval harness's missing <c>Critic</c> appsettings section meant the real critic couldn't
/// reject anything at all; see <c>appsettings.json</c>). Forced rejections are clearly marked as
/// such in their citation so a run's output never reads like a real critic finding.
/// </summary>
public sealed class FaultInjectingCriticStep(CriticStep innerCriticStep, CriticFaultInjectionState faultInjectionState) : ICriticStep
{
    public Task<CriticFinalReview> ReviewCandidateTrackAsync(CandidateTrack candidateTrack, GenerationJob generationJob,
        MatchedStyle matchedStyle, IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales,
        CancellationToken cancellationToken = default)
    {
        if (faultInjectionState.RejectionsToForce > 0)
        {
            faultInjectionState.RejectionsToForce--;
            CriticFinalReview forcedRejection = new(
                Pass: false,
                Score: 0,
                Citations: [new CriticCitation(CriticDimension.RhythmAndFeel, "Eval harness fault injection — not a real critic finding", CitedKnowledgeBaseItemId: string.Empty)]);
            return Task.FromResult(forcedRejection);
        }

        return innerCriticStep.ReviewCandidateTrackAsync(candidateTrack, generationJob, matchedStyle, matchedChords, matchedScales, cancellationToken);
    }
}