using System.Collections.Concurrent;

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

    public async Task CancelAsync(Guid jobId)
    {
        if (_jobRuns.TryGetValue(jobId, out StreamingRun? run))
        {
            // Mark Cancelled here, before actually cancelling the run: the workflow engine's
            // event stream ends as soon as CancelRunAsync is called, racing ahead of the executor's
            // own cooperative GenerationCancelled yield - so the worker can't reliably infer
            // cancellation from the run's output alone.
            GenerationJob? job = TryGet(jobId);
            if (job is not null)
            {
                AddOrUpdate(job with { State = JobStates.Cancelled, EndTime = DateTimeOffset.UtcNow });
            }

            logger.LogInformation("Cancellation requested for job {Id}", jobId);
            await run.CancelRunAsync();
            return;
        }

        // No run registered yet means the worker hasn't picked the job up - mark it
        // Cancelled directly so it gets skipped once it is dequeued.
        GenerationJob? pendingJob = TryGet(jobId);
        if (pendingJob is { State: JobStates.Pending })
        {
            logger.LogInformation("Cancellation requested for job {Id} before it started", jobId);
            AddOrUpdate(pendingJob with { State = JobStates.Cancelled, EndTime = DateTimeOffset.UtcNow });
        }
    }

    public void AppendJobFeedback(Guid jobId, JobFeedback newFeedbackItem)
    {
        try
        {
            _generationJobs.AddOrUpdate(jobId,
                _ => throw new KeyNotFoundException($"Job {jobId} was not found"),
                (_, existing) => existing with { FeedbackItems = [.. existing.FeedbackItems, newFeedbackItem] });
        }
        catch (KeyNotFoundException e)
        {
            logger.LogError(e, "Job {Id} was not found", jobId);
        }

    }
}