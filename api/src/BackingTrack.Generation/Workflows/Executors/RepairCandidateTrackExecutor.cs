using BackingTrack.Generation.Steps.TrackRepair;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class RepairCandidateTrackExecutor(
    IRepairStep repairStep,
    ILogger<RepairCandidateTrackExecutor> logger) : Executor("RepairCandidateTrackExecutor")
{
    internal async ValueTask HandleAsync(RepairCandidateTrackInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        await context.SendMessageAsync(new StatusUpdateInput(input.GenerationJob.Id, DateTime.UtcNow, "Repair",
            input.ValidationAttempts, "Repairing candidate track..."), cancellationToken);

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
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed during candidate track repair during attempt {Attempt}", input.GenerationJob.Id, input.ValidationAttempts);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message, input.ValidationAttempts), cancellationToken);
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
            .SendsMessage<StatusUpdateInput>()
            .YieldsOutput<GenerationOutcome>();
}