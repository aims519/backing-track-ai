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
        CandidateTrack repairedTrack;
        try
        {
            repairedTrack = await repairStep.RepairCandidateTrackAsync(input.CandidateTrack,
                input.ValidationResult.Failures, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Service shutdown, not a job failure - let it propagate and stop the loop.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed during candidate track repair during attempt {Attempt}", input.GenerationJob.Id, input.Attempts);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message, input.Attempts), cancellationToken);
            return;
        }

        int attemptCount = input.Attempts + 1;
        await context.SendMessageAsync(new ValidateCandidateTrackInput(repairedTrack, input.GenerationJob,
            input.MatchedChords, input.MatchedScales, attemptCount), cancellationToken);
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<RepairCandidateTrackInput>(HandleAsync))
            .SendsMessage<ValidateCandidateTrackInput>()
            .YieldsOutput<GenerationOutcome>();
}