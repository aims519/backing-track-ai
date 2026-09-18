using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Workflows.Executors;

public class FallbackGenerateCandidateTrackExecutor(IFallbackGenerationStep fallbackGenerationStep, ILogger<FallbackGenerateCandidateTrackExecutor> logger) : Executor("FallbackGenerateCandidateTrackExecutor")
{
    internal async ValueTask HandleAsync(FallbackGenerateCandidateTrackInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            GenerationJob job = input.GenerationJob;
            CandidateTrack track = await fallbackGenerationStep.GenerateFallbackCandidateTrackAsync(job.TimeSignatureBeatCount,
                job.TimeSignatureBeatUnit,
                input.MatchedStyle,
                input.MatchedChords,
                cancellationToken);

            await context.SendMessageAsync(new ValidateCandidateTrackInput(track,
                job,
                input.MatchedStyle,
                input.MatchedChords,
                input.MatchedScales,
                1,
                0,
                IsFallbackTrack: true), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutdown, not a job failure
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed during fallback track generation", input.GenerationJob.Id);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message), cancellationToken);
        }
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<FallbackGenerateCandidateTrackInput>(HandleAsync))
            .SendsMessage<ValidateCandidateTrackInput>()
            .YieldsOutput<GenerationOutcome>();
}