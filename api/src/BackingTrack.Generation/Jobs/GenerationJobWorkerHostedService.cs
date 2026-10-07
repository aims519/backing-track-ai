using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

using BackingTrack.Generation.Notifications;
using BackingTrack.Generation.Steps.FallbackGeneration;
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
            try
            {
                await ProcessGenerationJobAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Anything that escapes ProcessGenerationJobAsync (workflow construction, DI resolution,
                // the engine itself, a failing push) would otherwise fault this BackgroundService, and
                // the host's default behaviour is to stop the whole app - taking every queued job with it.
                logger.LogError(ex, "Job {Id} failed with an unhandled exception", jobId);

                // Only fail a job that hasn't reached a terminal state: a throw from the push
                // notification after the job completed or was cancelled mustn't rewrite that outcome.
                bool markedFailed = false;
                jobStore.UpdateJob(jobId, current =>
                {
                    markedFailed = current.State is JobStates.Pending or JobStates.InProgress;
                    return markedFailed ? current with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow } : current;
                });

                if (markedFailed)
                {
                    TryRecordJobOutcome(jobId);
                    await TrySendFailurePushAsync(jobId);
                }
            }
        }
    }

    // Best-effort: the exception being handled may well have come from the scope or the emitter,
    // so use a fresh scope and swallow anything this throws. Only called when the catch-all itself
    // failed the job - a job the normal path already failed has been pushed once, and shouldn't be again.
    private async Task TrySendFailurePushAsync(Guid jobId)
    {
        try
        {
            using IServiceScope scope = serviceScopeFactory.CreateScope();
            IPushNotificationEmitter emitter = scope.ServiceProvider.GetRequiredService<IPushNotificationEmitter>();
            if (jobStore.TryGet(jobId) is { } failedJob)
            {
                await emitter.SendApnsPushNotificationAsync(failedJob);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not send the failure push notification for job {Id}", jobId);
        }
    }

    private async Task ProcessGenerationJobAsync(Guid jobId, CancellationToken stoppingToken)
    {
        // Atomic Pending -> InProgress, so a /cancel landing after this check can't be overwritten.
        if (!jobStore.TryStartJob(jobId, out GenerationJob? startedJob))
        {
            // Record cancelled while pending, not visible elsewhere
            if (jobStore.TryGet(jobId) is { State: JobStates.Cancelled })
                TryRecordJobOutcome(jobId);

            logger.LogInformation("Job {Id} does not exist or was not pending (e.g. cancelled before it started), skipping", jobId);
            return;
        }

        using Activity? activity = GenerationJobActivitySource.Instance.StartActivity($"{nameof(GenerationJobWorkerHostedService)}.ProcessGenerationJobAsync");
        activity?.SetTag("job.id", startedJob.Id);
        activity?.SetTag("job.mode", startedJob.Mode);

        logger.LogInformation("Job {Id} started", jobId);

        using IServiceScope scope = serviceScopeFactory.CreateScope();

        Workflow generationWorkflow = GenerationWorkflowBuilder.Build(scope.ServiceProvider);
        IPushNotificationEmitter pushNotificationEmitter = scope.ServiceProvider.GetRequiredService<IPushNotificationEmitter>();

        StreamingRun? run = null;
        try
        {
            run = await InProcessExecution.RunStreamingAsync(generationWorkflow, startedJob, cancellationToken: stoppingToken);
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
                    // `UpdateJob`, not a `TryGet` snapshot plus `AddOrUpdate` - IJobStatusEmitter
                    // can still be appending FeedbackItems while this runs, and a snapshot-then-
                    // overwrite has a window where a concurrent append lands after the snapshot was
                    // read but before this write lands, and gets silently discarded.
                    jobStore.UpdateJob(jobId, current => current with
                    {
                        State = succeeded.FallbackReason.HasValue ? JobStates.CompletedViaFallback : JobStates.Complete,
                        EndTime = DateTimeOffset.UtcNow,
                        TrackId = succeeded.TrackId
                    });

                    if (TryRecordJobOutcome(jobId, succeeded.FallbackReason) is { } completedJob)
                        await pushNotificationEmitter.SendApnsPushNotificationAsync(completedJob);

                    logger.LogDebug("Job {Id} completed track saved: {Track}", startedJob.Id, JsonSerializer.Serialize(succeeded.CandidateTrack, CandidateTrackJsonContext.Default.CandidateTrack));
                    logger.LogInformation("Job {Id} completed", jobId);

                    generationJobMetrics.RecordAttempts(succeeded.Attempts, "success");
                    break;

                case GenerationFailed failed when !alreadyCancelled:
                    jobStore.UpdateJob(jobId, current => current with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow });

                    if (TryRecordJobOutcome(jobId) is { } failedJob)
                        await pushNotificationEmitter.SendApnsPushNotificationAsync(failedJob);

                    activity?.SetStatus(ActivityStatusCode.Error, failed.Reason);
                    logger.LogError("Job {Id} failed: {Reason}", jobId, failed.Reason);

                    if (failed.Attempts.HasValue)
                        generationJobMetrics.RecordAttempts(failed.Attempts.Value, "failed");
                    break;

                // `null` is included: no terminal outcome may have been observed before the job was marked
                // Cancelled - the workflow's event stream can end before an executor's own cooperative
                // GenerationCancelled yield reaches it, so which of these we land in is a race. They share
                // one case so the job is counted exactly once whichever way it goes.
                case GenerationCancelled or GenerationSucceeded or GenerationFailed or null when alreadyCancelled:
                    activity?.SetStatus(ActivityStatusCode.Error);
                    logger.LogInformation("Job {Id} was cancelled", jobId);

                    TryRecordJobOutcome(jobId);
                    break;

                case null:
                    logger.LogError("Job {Id} failed unexpectedly without a given reason", jobId);
                    activity?.SetStatus(ActivityStatusCode.Error);

                    jobStore.UpdateJob(jobId, current => current with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow });

                    if (TryRecordJobOutcome(jobId) is { } unexpectedFailedJob)
                        await pushNotificationEmitter.SendApnsPushNotificationAsync(unexpectedFailedJob);
                    break;
            }
        }
        finally
        {
            jobStore.UnregisterJobRun(jobId);
            if (run is not null) await run.DisposeAsync();
        }
    }

    // Records the outcome, then returns the job for the caller's push notification. Callers must record
    // first: a throwing push escapes to the catch-all, which ignores jobs already in a terminal state.
    private GenerationJob? TryRecordJobOutcome(Guid jobId, FallbackReason? fallbackReason = null)
    {
        if (jobStore.TryGet(jobId) is not { } job)
            return null;

        generationJobMetrics.RecordOutcome(job.State, job.Mode, job.EndTime - job.StartTime, fallbackReason);
        return job;
    }
}