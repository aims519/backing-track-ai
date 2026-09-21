using System.Diagnostics;
using System.Text.Json;

using BackingTrack.Core.Config;
using BackingTrack.Generation.Steps;
using BackingTrack.Generation.Steps.TrackPersistence;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class PersistCandidateTrackExecutor(
    IOptions<TrackStorageSettings> trackStorageSettings,
    ILogger<PersistCandidateTrackExecutor> logger) : Executor("PersistCandidateTrackExecutor")
{
    internal async ValueTask HandleAsync(PersistCandidateTrackInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        using Activity? activity = TrackPersistenceActivitySource.Instance.StartActivity($"{nameof(PersistCandidateTrackExecutor)}.{nameof(HandleAsync)}");
        activity?.SetTag("job.id", input.GenerationJob.Id);

        Guid trackId = Guid.NewGuid();
        activity?.SetTag("track.id", trackId);
        try
        {
            Directory.CreateDirectory(trackStorageSettings.Value.BaseFolderPath);
            string trackJson = JsonSerializer.Serialize(input.CandidateTrack, CandidateTrackJsonContext.Default.CandidateTrack);
            string trackFilePath = Path.Combine(trackStorageSettings.Value.BaseFolderPath, $"{trackId}.json");

            activity?.SetTag("track.filePath", trackFilePath);
            await File.WriteAllTextAsync(trackFilePath, trackJson, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Cancellation requested during {ExecutorName}", nameof(PersistCandidateTrackExecutor));
            await context.YieldOutputAsync(new GenerationCancelled(), CancellationToken.None);
            activity?.SetStatus(ActivityStatusCode.Error, "Job cancelled");
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed to persist the candidate track", input.GenerationJob.Id);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message, input.ValidationAttempts), cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return;
        }

        await context.YieldOutputAsync(new GenerationSucceeded(trackId, input.CandidateTrack, input.ValidationAttempts, input.WasFallback, input.CriticAttempts), cancellationToken);
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<PersistCandidateTrackInput>(HandleAsync))
            .YieldsOutput<GenerationOutcome>();
}