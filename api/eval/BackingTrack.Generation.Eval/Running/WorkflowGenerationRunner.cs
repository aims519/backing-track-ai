using System.Diagnostics;

using BackingTrack.Generation.Eval.Model;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Workflows;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Eval.Running;

/// <summary>
/// Drives the real in-process generation workflow — the same <see cref="Workflow"/> the API's
/// background worker builds and runs, minus the job store and queue. Running the actual pipeline
/// (rather than a reimplementation of it) is the whole point: the harness measures what ships.
/// </summary>
public sealed class WorkflowGenerationRunner(IServiceScopeFactory scopeFactory) : IGenerationRunner
{
    public async Task<GenerationRunResult> RunAsync(EvalCase evalCase, CancellationToken cancellationToken)
    {
        GenerationJob job = ToJob(evalCase.Input);
        long startTimestamp = Stopwatch.GetTimestamp();

        // A fresh scope per case, mirroring GenerationJobWorkerHostedService — the workflow
        // executors are scoped and carry per-run state.
        using IServiceScope scope = scopeFactory.CreateScope();
        Workflow workflow = GenerationWorkflowBuilder.Build(scope.ServiceProvider);

        Run run = await InProcessExecution.RunAsync(workflow, job, cancellationToken: cancellationToken);

        GenerationOutcome? outcome = run.OutgoingEvents
            .OfType<WorkflowOutputEvent>()
            .Select(e => e.As<GenerationOutcome>())
            .OfType<GenerationOutcome>()
            .LastOrDefault();

        TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);

        return outcome switch
        {
            GenerationSucceeded succeeded => new GenerationRunResult
            {
                Outcome = ActualOutcome.Succeeded,
                Attempts = succeeded.Attempts,
                TrackId = succeeded.TrackId,
                Elapsed = elapsed
            },
            GenerationFailed failed => new GenerationRunResult
            {
                Outcome = ActualOutcome.Failed,
                Attempts = failed.Attempts,
                FailureReason = failed.Reason,
                Elapsed = elapsed
            },
            _ => new GenerationRunResult
            {
                Outcome = ActualOutcome.Failed,
                FailureReason = "workflow produced no GenerationOutcome",
                Elapsed = elapsed
            }
        };
    }

    private static GenerationJob ToJob(GenerationInput input) => new()
    {
        Id = Guid.NewGuid(),
        State = JobStates.Pending,
        StartTime = DateTimeOffset.UtcNow,
        Mode = Enum.Parse<GenerationMode>(input.Mode),
        Tempo = input.Tempo,
        Chords = input.Chords,
        KeyRoot = input.KeyRoot,
        Style = input.Style,
        TimeSignatureBeatCount = input.TimeSignatureBeatCount,
        TimeSignatureBeatUnit = input.TimeSignatureBeatUnit
    };
}