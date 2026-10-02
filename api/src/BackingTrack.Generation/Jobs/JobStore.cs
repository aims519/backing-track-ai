using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Jobs;

public class JobStore(ILogger<JobStore> logger)
{
    private readonly ConcurrentDictionary<Guid, GenerationJob> _generationJobs = new();
    private readonly ConcurrentDictionary<Guid, StreamingRun> _jobRuns = new();

    public void AddOrUpdate(GenerationJob job)
    {
        _generationJobs.AddOrUpdate(job.Id, job, (_, _) => job);
    }

    public void Remove(Guid jobId)
    {
        _generationJobs.TryRemove(jobId, out _);
    }

    public GenerationJob? TryGet(Guid jobId)
    {
        _generationJobs.TryGetValue(jobId, out GenerationJob? foundJob);
        return foundJob;
    }

    public void RegisterJobRun(Guid jobId, StreamingRun run) => _jobRuns.AddOrUpdate(jobId, run, (_, _) => run);

    public void UnregisterJobRun(Guid jobId) => _jobRuns.TryRemove(jobId, out _);

    /// Returns false if no such job exists (the endpoint maps that to 404); true otherwise, including
    /// for a job that has already finished, so cancelling twice or too late stays idempotent.
    public async Task<bool> CancelAsync(Guid jobId)
    {
        if (TryGet(jobId) is null)
        {
            return false;
        }

        if (_jobRuns.TryGetValue(jobId, out StreamingRun? run))
        {
            // Mark Cancelled here, before actually cancelling the run: the workflow engine's
            // event stream ends as soon as CancelRunAsync is called, racing ahead of the executor's
            // own cooperative GenerationCancelled yield - so the worker can't reliably infer
            // cancellation from the run's output alone.
            if (TryGet(jobId) is not null)
            {
                UpdateJob(jobId, MarkCancelled);
            }

            logger.LogInformation("Cancellation requested for job {Id}", jobId);
            await run.CancelRunAsync();
            return true;
        }

        // No run registered yet means the worker hasn't picked the job up - mark it
        // Cancelled directly so it gets skipped once it is dequeued. The Pending check is
        // repeated inside the atomic update so a worker transition landing after the check
        // isn't overwritten.
        if (TryGet(jobId) is { State: JobStates.Pending })
        {
            logger.LogInformation("Cancellation requested for job {Id} before it started", jobId);
            UpdateJob(jobId, job => job.State == JobStates.Pending ? MarkCancelled(job) : job);
        }

        return true;
    }

    private static GenerationJob MarkCancelled(GenerationJob job) =>
        job with { State = JobStates.Cancelled, EndTime = DateTimeOffset.UtcNow };

    /// Atomically moves a job from Pending to InProgress. Returns false, leaving the job untouched,
    /// if it is missing or in any other state - notably Cancelled, which a `TryGet` check followed by
    /// `AddOrUpdate(snapshot with { State = InProgress })` would silently overwrite if `CancelAsync`
    /// landed in between. `TryUpdate` compares by reference, so a concurrent `AppendJobFeedback`
    /// (which swaps in a new instance) makes it retry instead of being discarded.
    public bool TryStartJob(Guid jobId, [NotNullWhen(true)] out GenerationJob? startedJob)
    {
        while (_generationJobs.TryGetValue(jobId, out GenerationJob? existing) && existing.State == JobStates.Pending)
        {
            startedJob = existing with { State = JobStates.InProgress };
            if (_generationJobs.TryUpdate(jobId, startedJob, existing))
            {
                return true;
            }
        }

        startedJob = null;
        return false;
    }

    public void AppendJobFeedback(Guid jobId, JobFeedback newFeedbackItem)
    {
        UpdateJob(jobId, existing => existing with { FeedbackItems = [.. existing.FeedbackItems, newFeedbackItem] });
    }

    /// Atomically applies `update` to whatever's currently stored for `jobId`. Unlike `AddOrUpdate`,
    /// which replaces the entry with a caller-supplied snapshot regardless of what's there, this
    /// reads-and-writes as one operation - so it can't lose a concurrent `AppendJobFeedback` call the
    /// way a `TryGet` snapshot followed by `AddOrUpdate` can. Use this for any transition made after
    /// the job has started running, since `IJobStatusEmitter` can still be appending feedback for as
    /// long as the workflow is executing.
    public void UpdateJob(Guid jobId, Func<GenerationJob, GenerationJob> update)
    {
        try
        {
            _generationJobs.AddOrUpdate(jobId,
                _ => throw new KeyNotFoundException($"Job {jobId} was not found"),
                (_, existing) => update(existing));
        }
        catch (KeyNotFoundException e)
        {
            logger.LogError(e, "Job {Id} was not found", jobId);
        }
    }

    public int JobCount => _generationJobs.Count;

    public int EvictTerminatedJobs(DateTimeOffset cutoff)
    {
        int evictedJobsCount = 0;
        IEnumerable<KeyValuePair<Guid, GenerationJob>> keyValuePairsToRemove =
            _generationJobs
                .Where(p => IsJobTerminated(p.Value))
                .Where(p => p.Value.EndTime < cutoff);

        foreach (KeyValuePair<Guid, GenerationJob> keyValuePair in keyValuePairsToRemove)
        {
            if (_generationJobs.TryRemove(keyValuePair))
                evictedJobsCount++;
        }

        return evictedJobsCount;
    }

    private bool IsJobTerminated(GenerationJob job) =>
        job.State is JobStates.Cancelled
            or JobStates.Complete
            or JobStates.CompletedViaFallback
            or JobStates.Failed;
}