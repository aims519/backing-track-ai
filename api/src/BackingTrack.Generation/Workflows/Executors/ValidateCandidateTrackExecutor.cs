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
            await context.SendMessageAsync(new StatusUpdateInput(input.GenerationJob.Id, DateTime.UtcNow, "Validation",
                input.ValidationAttempts, "Validating candidate track..."), cancellationToken);

            TrackValidationResult trackValidationResult = validateStep.ValidateCandidateTrack(input.CandidateTrack,
                input.GenerationJob, input.MatchedChords, input.MatchedScales);

            // Pass validation on a regular, non-fallback track
            if (trackValidationResult.IsValid && !input.IsFallbackTrack)
            {
                await context.SendMessageAsync(new CriticInput(input.CandidateTrack,
                    input.GenerationJob,
                    input.MatchedStyle,
                    input.MatchedChords,
                    input.MatchedScales,
                    input.ValidationAttempts,
                    input.CriticAttempts), cancellationToken);
                return;
            }

            // Pass validation of a fallback track, straight to persistence
            if (trackValidationResult.IsValid && input.IsFallbackTrack)
            {
                await context.SendMessageAsync(new PersistCandidateTrackInput(input.CandidateTrack,
                    input.GenerationJob,
                    input.ValidationAttempts,
                    input.CriticAttempts,
                    WasFallback: true), cancellationToken);
                return;
            }

            // Failed validation of a fallback track
            if (!trackValidationResult.IsValid && input.IsFallbackTrack)
            {
                logger.LogError("Job {Id} failed validation of a fallback candidate track", input.GenerationJob.Id);
                await context.YieldOutputAsync(new GenerationFailed($"Job {input.GenerationJob.Id} failed validation of a fallback candidate track"), cancellationToken);
                return;
            }

            logger.LogDebug("Job {Id} attempt {Attempt} candidate track: {Track}", input.GenerationJob.Id, input.ValidationAttempts,
                JsonSerializer.Serialize(input.CandidateTrack, CandidateTrackJsonContext.Default.CandidateTrack));
            logger.LogWarning("Job {Id} attempt {Attempt} failed validation: {Errors}", input.GenerationJob.Id, input.ValidationAttempts, string.Join('\n', trackValidationResult.Failures.Select(f => f.Reason)));

            // Failed with max attempts reached, fallback to rule-based generation
            if (input.ValidationAttempts >= trackGenerationWorkflowSettings.Value.GenerationMaxAttempts)
            {
                logger.LogWarning("Job {Id} track generation exhausted all {Attempts} attempts and failed validation.\n{Errors}", input.GenerationJob.Id, trackGenerationWorkflowSettings.Value.GenerationMaxAttempts, string.Join('\n', trackValidationResult?.Failures.Select(f => f.Reason) ?? Array.Empty<string>()));
                await context.SendMessageAsync(new FallbackGenerateCandidateTrackInput(input.GenerationJob,
                    input.MatchedStyle,
                    input.MatchedChords,
                    input.MatchedScales), cancellationToken);
                return;
            }

            // Failed with retry, send Repair Message
            await context.SendMessageAsync(new RepairCandidateTrackInput(input.CandidateTrack, trackValidationResult, input.GenerationJob, input.MatchedStyle, input.MatchedChords, input.MatchedScales, input.ValidationAttempts, input.CriticAttempts), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Cancellation requested during {ExecutorName}", nameof(ValidateCandidateTrackExecutor));
            await context.YieldOutputAsync(new GenerationCancelled(), CancellationToken.None);
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
            .SendsMessage<CriticInput>()
            .SendsMessage<FallbackGenerateCandidateTrackInput>()
            .SendsMessage<PersistCandidateTrackInput>()
            .SendsMessage<StatusUpdateInput>()
            .YieldsOutput<GenerationOutcome>();
}