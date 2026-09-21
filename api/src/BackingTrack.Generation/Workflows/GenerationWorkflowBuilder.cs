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
        FallbackGenerateCandidateTrackExecutor fallback = scopedProvider.GetRequiredService<FallbackGenerateCandidateTrackExecutor>();
        RepairCandidateTrackExecutor repair = scopedProvider.GetRequiredService<RepairCandidateTrackExecutor>();
        CriticExecutor critic = scopedProvider.GetRequiredService<CriticExecutor>();
        PersistCandidateTrackExecutor persist = scopedProvider.GetRequiredService<PersistCandidateTrackExecutor>();
        StatusUpdateExecutor statusUpdate = scopedProvider.GetRequiredService<StatusUpdateExecutor>();

        return new WorkflowBuilder(style)
            .AddEdge(style, chords)
            .AddEdge(chords, scales)
            .AddEdge(scales, generate)
            .AddEdge(generate, validate)
            .AddEdge(validate, repair)
            .AddEdge(validate, fallback)
            .AddEdge(fallback, validate)
            .AddEdge(repair, validate)
            .AddEdge(validate, critic)
            .AddEdge(validate, persist)
            .AddEdge(critic, persist)
            .AddEdge(critic, generate)
            .AddEdge(critic, fallback)
            .WithOutputFrom(style, chords, scales, generate, validate, fallback, repair, critic, persist)

            // Status update edges
            .AddEdge(style, statusUpdate)
            .AddEdge(chords, statusUpdate)
            .AddEdge(scales, statusUpdate)
            .AddEdge(generate, statusUpdate)
            .AddEdge(validate, statusUpdate)
            .AddEdge(fallback, statusUpdate)
            .AddEdge(repair, statusUpdate)
            .AddEdge(critic, statusUpdate)
            .AddEdge(persist, statusUpdate)
            .Build();
    }
}