using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Retrieval;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class RetrieveStyleExecutor(IRetrieveStep retrieveStep,
    IJobStatusEmitter statusEmitter,
    ILogger<RetrieveStyleExecutor> logger) : Executor("RetrieveStyleExecutor")
{
    internal async ValueTask HandleAsync(GenerationJob job, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        statusEmitter.Emit(job.Id, "Retrieval", 1, "Retrieving musical style information...");

        MatchedStyle? matchedStyle;
        try
        {
            matchedStyle = await retrieveStep.RetrieveTopMatchingStyles(job.Style, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Cancellation requested during {ExecutorName}", nameof(RetrieveStyleExecutor));
            await context.YieldOutputAsync(new GenerationCancelled(), CancellationToken.None);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed during style retrieval", job.Id);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message), cancellationToken);
            return;
        }

        if (matchedStyle == null)
        {
            logger.LogWarning("Job {Id} found no matching style entry for style '{Style}'", job.Id, job.Style);
            await context.YieldOutputAsync(new GenerationFailed($"No matching style entry for '{job.Style}'"), cancellationToken);
            return;
        }

        logger.LogInformation("Job {Id} found matching style entry, {Style}", job.Id, matchedStyle.StyleEntry.Id);
        await context.SendMessageAsync(new RetrieveChordsInput(job, matchedStyle), cancellationToken: cancellationToken);
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<GenerationJob>(HandleAsync))
            .SendsMessage<RetrieveChordsInput>()
            .YieldsOutput<GenerationOutcome>();
}