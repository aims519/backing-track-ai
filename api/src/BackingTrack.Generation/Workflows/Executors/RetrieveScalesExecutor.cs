using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Retrieval;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class RetrieveScalesExecutor(
    IRetrieveStep retrieveStep,
    ILogger<RetrieveScalesExecutor> logger) : Executor("RetrieveScalesExecutor")
{
    internal async ValueTask HandleAsync(RetrieveScalesInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MatchedScale> matchingScaleEntries;
        try
        {
            matchingScaleEntries = retrieveStep.RetrieveMatchingScales(input.GenerationJob.KeyRoot,
                    input.MatchedChords,
                    cancellationToken)
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Service shutdown, not a job failure - let it propagate and stop the loop.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed during scale retrieval", input.GenerationJob.Id);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message), cancellationToken);
            return;
        }
            
        if(!matchingScaleEntries.Any())
            logger.LogInformation("Job {Id} has found no matching scales",  input.GenerationJob.Id);
        else
            logger.LogInformation("Job {Id} found matching scales: {ScalesString}", input.GenerationJob.Id, 
                string.Join(',', matchingScaleEntries.Select(s => $"{s.Chord.Root}{s.ScaleEntry.Id}/{s.Chord.Root}{s.Chord.ChordEntry.Id}")));

        await context.SendMessageAsync(new GenerateCandidateTrackInput(input.GenerationJob, input.MatchedStyle, input.MatchedChords, matchingScaleEntries), cancellationToken);
    }
    
    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) => 
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<RetrieveScalesInput>(HandleAsync))
            .SendsMessage<GenerateCandidateTrackInput>()
            .YieldsOutput<GenerationOutcome>();
}