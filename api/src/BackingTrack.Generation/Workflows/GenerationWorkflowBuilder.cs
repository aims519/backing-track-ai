using BackingTrack.Generation.Workflows.Executors;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Workflows;

public static class GenerationWorkflowBuilder
{
    public static Workflow Build(IServiceProvider scopedProvider)
    {
        RetrieveStyleExecutor style = scopedProvider.GetRequiredService<RetrieveStyleExecutor>();
        RetrieveChordsExecutor chords = scopedProvider.GetRequiredService<RetrieveChordsExecutor>();
        RetrieveScalesExecutor scales = scopedProvider.GetRequiredService<RetrieveScalesExecutor>();
        GenerateCandidateTrackExecutor generate = scopedProvider.GetRequiredService<GenerateCandidateTrackExecutor>();
        ValidateCandidateTrackExecutor validate = scopedProvider.GetRequiredService<ValidateCandidateTrackExecutor>();
        RepairCandidateTrackExecutor repair = scopedProvider.GetRequiredService<RepairCandidateTrackExecutor>();
        PersistCandidateTrackExecutor persist = scopedProvider.GetRequiredService<PersistCandidateTrackExecutor>();

        return new WorkflowBuilder(style)
            .AddEdge(style, chords)
            .AddEdge(chords, scales)
            .AddEdge(scales, generate)
            .AddEdge(generate, validate)
            .AddEdge(validate, repair)
            .AddEdge(repair, validate)
            .AddEdge(validate, persist)
            .WithOutputFrom(style, chords, scales, generate, validate, repair, persist)
            .Build();
    }
}