using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Jobs;

public class GenerationJobWorkerHostedService(JobStore jobStore,
    GenerationJobQueue jobQueue,
    ILogger<GenerationJobWorkerHostedService> logger,
    GenerationJobMetrics generationJobMetrics,
    IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    private readonly ChannelReader<Guid> _jobReader = jobQueue.Reader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Guid jobId in _jobReader.ReadAllAsync(stoppingToken))
        {
            await ProcessGenerationJobAsync(jobId, stoppingToken);
        }
    }

    private async Task ProcessGenerationJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        GenerationJob? generationJob = jobStore.TryGet(jobId);
        if (generationJob == null)
        {
            logger.LogInformation("Job {Id} does not exist", jobId);
            return;
        }

        if (generationJob.State == JobStates.Cancelled)
        {
            logger.LogInformation("Job {Id} was cancelled before it started", jobId);
            return;
        }

        using Activity? activity = GenerationJobActivitySource.Instance.StartActivity($"{nameof(GenerationJobWorkerHostedService)}.ProcessGenerationJobAsync");
        activity?.SetTag("job.id", generationJob.Id);
        activity?.SetTag("job.mode", generationJob.Mode);

        GenerationJob updatedJob = generationJob with { State = JobStates.InProgress };
        jobStore.AddOrUpdate(updatedJob);

        logger.LogInformation("Job {Id} started", jobId);

        // Process the generation job here
        // Update the job state as needed
        using IServiceScope scope = serviceScopeFactory.CreateScope();

        Workflow generationWorkflow = GenerationWorkflowBuilder.Build(scope.ServiceProvider);

        StreamingRun? run = null;
        try
        {
            run = await InProcessExecution.RunStreamingAsync(generationWorkflow, generationJob, cancellationToken: stoppingToken);
            jobStore.RegisterJobRun(jobId, run);

            GenerationOutcome? outcome = null;
            await foreach (WorkflowEvent evt in run.WatchStreamAsync(blockOnPendingRequest: false, stoppingToken))
            {
                if (evt is WorkflowOutputEvent output && output.As<GenerationOutcome>() is { } o)
                    outcome = o;
            }

            // /cancel marks the job Cancelled directly as soon as it's requested (see JobStore.CancelAsync) -
            // the workflow engine's event stream can end before, or race with, an executor's own cooperative
            // GenerationCancelled yield, so a terminal outcome arriving after that point must not clobber it.
            bool alreadyCancelled = jobStore.TryGet(jobId)?.State == JobStates.Cancelled;

            switch (outcome)
            {
                case GenerationSucceeded succeeded when !alreadyCancelled:
                    GenerationJob completedJob = updatedJob with
                    {
                        State = succeeded.WasFallback ? JobStates.CompletedViaFallback : JobStates.Complete,
                        EndTime = DateTimeOffset.UtcNow,
                        TrackId = succeeded.TrackId
                    };
                    jobStore.AddOrUpdate(completedJob);

                    logger.LogDebug("Job {Id} completed track saved: {Track}", generationJob.Id, JsonSerializer.Serialize(succeeded.CandidateTrack, CandidateTrackJsonContext.Default.CandidateTrack));
                    logger.LogInformation("Job {Id} completed", jobId);

                    generationJobMetrics.RecordAttempts(succeeded.Attempts, "success");
                    break;

                case GenerationFailed failed when !alreadyCancelled:
                    GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                    jobStore.AddOrUpdate(failedJob);

                    activity?.SetStatus(ActivityStatusCode.Error, failed.Reason);
                    logger.LogError("Job {Id} failed: {Reason}", jobId, failed.Reason);

                    if (failed.Attempts.HasValue)
                    {
                        generationJobMetrics.RecordAttempts(failed.Attempts.Value, "failed");
                    }
                    break;

                case GenerationCancelled or GenerationSucceeded or GenerationFailed when alreadyCancelled:
                    activity?.SetStatus(ActivityStatusCode.Error);
                    logger.LogInformation("Job {Id} was cancelled", jobId);
                    break;

                case null when alreadyCancelled:
                    // No terminal outcome was observed before the job was marked Cancelled - the
                    // workflow's event stream can end before an executor's own cooperative
                    // GenerationCancelled yield reaches it, so this doesn't mean cancellation
                    // landed between steps, just that we didn't wait to see how it landed.
                    activity?.SetStatus(ActivityStatusCode.Error);
                    logger.LogInformation("Job {Id} was cancelled", jobId);
                    break;

                case null:
                    logger.LogError("Job {Id} failed unexpectedly without a given reason", jobId);
                    activity?.SetStatus(ActivityStatusCode.Error);

                    GenerationJob failedJobNoExp = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                    jobStore.AddOrUpdate(failedJobNoExp);
                    break;
            }
        }
        finally
        {
            jobStore.UnregisterJobRun(jobId);
            if (run is not null) await run.DisposeAsync();
        }
    }
}