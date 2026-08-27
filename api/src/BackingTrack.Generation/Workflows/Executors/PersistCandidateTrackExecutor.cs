using System.Text.Json;

using BackingTrack.Core.Config;
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
    private async ValueTask HandleAsync(PersistCandidateTrackInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        Guid trackId = Guid.NewGuid();
        try
        {
            Directory.CreateDirectory(trackStorageSettings.Value.BaseFolderPath);
            string trackJson = JsonSerializer.Serialize(input.CandidateTrack, CandidateTrackJsonContext.Default.CandidateTrack);
            string trackFilePath = Path.Combine(trackStorageSettings.Value.BaseFolderPath, $"{trackId}.json");
            await File.WriteAllTextAsync(trackFilePath, trackJson, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Service shutdown, not a job failure - let it propagate and stop the loop.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed to persist the candidate track", input.GenerationJob.Id);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message),  cancellationToken);
            return;
        }

        await context.YieldOutputAsync(new GenerationSucceeded(trackId),  cancellationToken);
    }
    
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => 
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<PersistCandidateTrackInput>(HandleAsync))
            .YieldsOutput<GenerationOutcome>();
}