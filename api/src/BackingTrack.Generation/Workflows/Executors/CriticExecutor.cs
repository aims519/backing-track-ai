using BackingTrack.Core.Config;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class CriticExecutor(ICriticStep criticStep, IOptions<CriticSettings> criticSettings, ILogger<CriticExecutor> logger) : Executor("CriticExecutor")
{
    internal async ValueTask HandleAsync(CriticInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            CriticFinalReview review = await criticStep.ReviewCandidateTrackAsync(input.CandidateTrack,
                input.GenerationJob,
                input.MatchedStyle,
                input.MatchedChords,
                input.MatchedScales,
                cancellationToken);

            if (!review.Pass)
            {
                logger.LogInformation("Critic review failed with score of {Score} and {Citations} citations. " +
                                      "Triggering new candidate track generation",
                    review.Score,  review.Citations.Count);
                logger.LogDebug("Citations:\n{Citations}",  string.Join('\n',  review.Citations.Select(c => $"{c.Dimension}: {c.Violation}")));

                int criticAttemptsUpdated = input.CriticAttempts + 1;

                // Failed with max attempts reached - stop here instead of spending another
                // generate+validate round only to reject it on the next critic review.
                if (criticAttemptsUpdated > criticSettings.Value.MaxAttempts)
                {
                    logger.LogError("Job {Id} track generation exhausted all {Attempts} critic review attempts and failed", input.GenerationJob.Id, criticSettings.Value.MaxAttempts);
                    await context.YieldOutputAsync(new GenerationFailed($"Max {criticSettings.Value.MaxAttempts} track critic review attempts reached", criticAttemptsUpdated), cancellationToken);
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
            logger.LogDebug("Citations:\n{Citations}",  string.Join('\n',  review.Citations.Select(c => $"{c.Dimension}: {c.Violation}")));
            
            await context.SendMessageAsync(new PersistCandidateTrackInput(input.CandidateTrack,input.GenerationJob, input.ValidationAttempts, input.CriticAttempts), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutdown, not a job failure
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed during critic review", input.GenerationJob.Id);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message), cancellationToken);
        }
    }
    
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<CriticInput>(HandleAsync))
            .SendsMessage<GenerateCandidateTrackInput>()
            .SendsMessage<PersistCandidateTrackInput>()
            .YieldsOutput<GenerationOutcome>();
}