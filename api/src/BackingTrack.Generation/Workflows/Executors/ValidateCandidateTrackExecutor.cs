using System.Text.Json;

using BackingTrack.Core.Config;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class ValidateCandidateTrackExecutor(
    IValidateStep validateStep,
    IOptions<TrackGenerationWorkflowSettings> trackGenerationWorkflowSettings,
    ILogger<ValidateCandidateTrackExecutor> logger) : Executor("ValidateCandidateTrackExecutor")
{
    internal async ValueTask HandleAsync(ValidateCandidateTrackInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            TrackValidationResult trackValidationResult = validateStep.ValidateCandidateTrack(input.CandidateTrack,
                input.GenerationJob, input.MatchedChords, input.MatchedScales);

            if (trackValidationResult.IsValid)
            {
                await context.SendMessageAsync(new PersistCandidateTrackInput(input.CandidateTrack, input.GenerationJob, input.Attempts), cancellationToken);
                return;
            }

            logger.LogDebug("Job {Id} attempt {Attempt} candidate track: {Track}", input.GenerationJob.Id, input.Attempts,
                JsonSerializer.Serialize(input.CandidateTrack, CandidateTrackJsonContext.Default.CandidateTrack));
            logger.LogWarning("Job {Id} attempt {Attempt} failed validation: {Errors}", input.GenerationJob.Id, input.Attempts, string.Join('\n', trackValidationResult.Failures.Select(f => f.Reason)));

            // Failed with max attempts reached
            if (input.Attempts >= trackGenerationWorkflowSettings.Value.GenerationMaxAttempts)
            {
                logger.LogError("Job {Id} track generation exhausted all {Attempts} attempts and failed validation.\n{Errors}", input.GenerationJob.Id, trackGenerationWorkflowSettings.Value.GenerationMaxAttempts, string.Join('\n', trackValidationResult?.Failures.Select(f => f.Reason) ?? Array.Empty<string>()));
                await context.YieldOutputAsync(new GenerationFailed($"Max {trackGenerationWorkflowSettings.Value.GenerationMaxAttempts} track generation attempts reached with failed validation", input.Attempts), cancellationToken);
                return;
            }

            // Failed with retry, send Repair Message
            await context.SendMessageAsync(new RepairCandidateTrackInput(input.CandidateTrack, trackValidationResult, input.GenerationJob, input.MatchedStyle, input.MatchedChords, input.MatchedScales, input.Attempts), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutdown, not a job failure
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed during candidate track validation", input.GenerationJob.Id);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message), cancellationToken);
        }
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<ValidateCandidateTrackInput>(HandleAsync))
            .SendsMessage<RepairCandidateTrackInput>()
            .SendsMessage<PersistCandidateTrackInput>()
            .YieldsOutput<GenerationOutcome>();
}