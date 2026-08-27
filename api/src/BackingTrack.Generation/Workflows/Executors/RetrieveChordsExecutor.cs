using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Retrieval;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class RetrieveChordsExecutor(
    IRetrieveStep retrieveStep,
    ILogger<RetrieveChordsExecutor> logger) : Executor("RetrieveChordsExecutor")
{
    internal async ValueTask HandleAsync(RetrieveChordsInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        GenerationJob job = input.Job;
        IReadOnlyList<MatchedChord> matchingChords;
        try
        {
            matchingChords = retrieveStep.RetrieveMatchingChords(input.Job.Chords);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed during chord retrieval", job.Id);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message), cancellationToken);
            return;
        }

        await context.SendMessageAsync(new RetrieveScalesInput(matchingChords, input.MatchedStyle, job), cancellationToken);
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<RetrieveChordsInput>(HandleAsync))
            .SendsMessage<RetrieveScalesInput>()
            .YieldsOutput<GenerationOutcome>();
}