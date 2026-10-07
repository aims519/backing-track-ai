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

        return new WorkflowBuilder(style)
            .AddEdge(style, chords)
            .AddEdge(chords, scales)
            .AddEdge(scales, generate)
            .AddEdge(generate, validate)
            .AddEdge(generate, fallback)
            .AddEdge(validate, repair)
            .AddEdge(validate, fallback)
            .AddEdge(fallback, validate)
            .AddEdge(repair, validate)
            .AddEdge(repair, fallback)
            .AddEdge(validate, critic)
            .AddEdge(validate, persist)
            .AddEdge(critic, persist)
            .AddEdge(critic, generate)
            .AddEdge(critic, fallback)
            .WithOutputFrom(style, chords, scales, generate, validate, fallback, repair, critic, persist)
            .Build();
    }
}