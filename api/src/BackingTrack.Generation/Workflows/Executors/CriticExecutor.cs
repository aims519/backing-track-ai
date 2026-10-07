using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class CriticExecutor(ICriticStep criticStep, IOptions<CriticSettings> criticSettings,
    IJobStatusEmitter statusEmitter, ILogger<CriticExecutor> logger) : Executor("CriticExecutor")
{
    internal async ValueTask HandleAsync(CriticInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            statusEmitter.Emit(input.GenerationJob.Id, "Critic Review", input.CriticAttempts,
                "Final musical review of candidate track...");

            CriticFinalReview review = await criticStep.ReviewCandidateTrackAsync(input.CandidateTrack,
                input.GenerationJob,
                input.MatchedStyle,
                input.MatchedChords,
                input.MatchedScales,
                cancellationToken);

            string outcomeMessage = review.Pass
                ? $"Critic review passed with score {review.Score:0}%"
                : $"Critic review failed with score {review.Score:0}% and {review.Citations.Count} citation{(review.Citations.Count == 1 ? "" : "s")}";
            statusEmitter.Emit(input.GenerationJob.Id, "Critic Review", input.CriticAttempts, outcomeMessage);

            if (!review.Pass)
            {
                logger.LogInformation("Critic review failed with score of {Score} and {Citations} citations. " +
                                      "Triggering new candidate track generation",
                    review.Score, review.Citations.Count);
                logger.LogDebug("Citations:\n{Citations}", string.Join('\n', review.Citations.Select(c => $"{c.Dimension}: {c.Violation}")));

                int criticAttemptsUpdated = input.CriticAttempts + 1;

                // Failed with max attempts reached - stop here instead of spending another
                // generate+validate round only to reject it on the next critic review, and fall
                // back to the deterministic rule-based generator instead of failing the job.
                if (criticAttemptsUpdated > criticSettings.Value.MaxAttempts)
                {
                    logger.LogWarning("Job {Id} track generation exhausted all {Attempts} critic review attempts, falling back to rule-based generation", input.GenerationJob.Id, criticSettings.Value.MaxAttempts);
                    await context.SendMessageAsync(new FallbackGenerateCandidateTrackInput(input.GenerationJob,
                        input.MatchedStyle,
                        input.MatchedChords,
                        input.MatchedScales,
                        FallbackReason.CriticExhausted), cancellationToken);
                    return;
                }

                await context.SendMessageAsync(new GenerateCandidateTrackInput(input.GenerationJob,
                    input.MatchedStyle,
                    input.MatchedChords,
                    input.MatchedScales,
                    CriticReview: review,
                    PreviousAttempt: input.CandidateTrack,
                    CriticAttempts: criticAttemptsUpdated), cancellationToken);
                return;
            }

            // Critic review passed, persist the track
            logger.LogInformation("Critic review passed with score of {Score}", review.Score);
            if (review.Citations.Count > 0)
            {
                logger.LogDebug("Citations:\n{Citations}", string.Join('\n', review.Citations.Select(c => $"{c.Dimension}: {c.Violation}")));
            }

            await context.SendMessageAsync(new PersistCandidateTrackInput(input.CandidateTrack, input.GenerationJob, input.ValidationAttempts, input.CriticAttempts), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Cancellation requested during {ExecutorName}", nameof(CriticExecutor));
            await context.YieldOutputAsync(new GenerationCancelled(), CancellationToken.None);
        }
        catch (Exception ex) // Unexpected errors should not fail the job so we fall back to rule-based generation (ADR 0034's "never fail" guarantee)
        {
            logger.LogError(ex, "Job {Id} failed during critic review during attempt {Attempt}. Falling back to rule-based generation", input.GenerationJob.Id, input.CriticAttempts);
            await context.SendMessageAsync(new FallbackGenerateCandidateTrackInput(input.GenerationJob, input.MatchedStyle, input.MatchedChords, input.MatchedScales, FallbackReason.CriticError), cancellationToken);
        }
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<CriticInput>(HandleAsync))
            .SendsMessage<GenerateCandidateTrackInput>()
            .SendsMessage<PersistCandidateTrackInput>()
            .SendsMessage<FallbackGenerateCandidateTrackInput>()
            .YieldsOutput<GenerationOutcome>();
}