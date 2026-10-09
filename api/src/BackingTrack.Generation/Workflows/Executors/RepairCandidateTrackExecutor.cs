using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.TrackRepair;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class RepairCandidateTrackExecutor(
    IRepairStep repairStep,
    IJobStatusEmitter statusEmitter,
    ILogger<RepairCandidateTrackExecutor> logger) : Executor("RepairCandidateTrackExecutor")
{
    internal async ValueTask HandleAsync(RepairCandidateTrackInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        int fixCount = input.ValidationResult.Failures.Count;
        statusEmitter.Emit(input.GenerationJob.Id, "Applying Repairs", input.CriticAttempts,
            $"Fixing {fixCount} track issue{(fixCount == 1 ? "" : "s")}...");

        CandidateTrack repairedTrack;
        try
        {
            repairedTrack = await repairStep.RepairCandidateTrackAsync(input.CandidateTrack,
                input.ValidationResult.Failures, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Cancellation requested during {ExecutorName}", nameof(RepairCandidateTrackExecutor));
            await context.YieldOutputAsync(new GenerationCancelled(), CancellationToken.None);
            return;
        }
        catch (Exception ex) // Unexpected errors should not fail the job so we fall back to rule-based generation (ADR 0034's "never fail" guarantee)
        {
            logger.LogError(ex, "Job {Id} failed during candidate track repair during attempt {Attempt}. Falling back to rule-based generation", input.GenerationJob.Id, input.ValidationAttempts);
            await context.SendMessageAsync(new FallbackGenerateCandidateTrackInput(input.GenerationJob, input.MatchedStyle, input.MatchedChords, input.MatchedScales, FallbackReason.RepairError), cancellationToken);
            return;
        }

        int attemptCount = input.ValidationAttempts + 1;
        await context.SendMessageAsync(new ValidateCandidateTrackInput(repairedTrack, input.GenerationJob,
            input.MatchedStyle, input.MatchedChords, input.MatchedScales, attemptCount, input.CriticAttempts), cancellationToken);
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<RepairCandidateTrackInput>(HandleAsync))
            .SendsMessage<ValidateCandidateTrackInput>()
            .SendsMessage<FallbackGenerateCandidateTrackInput>()
            .YieldsOutput<GenerationOutcome>();
}