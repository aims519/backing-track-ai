using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Tests.Workflows;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging.Abstractions;

namespace BackingTrack.Generation.Tests.Jobs;

public class JobStoreTests
{
    private static JobStore CreateStore() => new(NullLogger<JobStore>.Instance);

    [Fact]
    public async Task CancelAsync_JobIsPendingAndNotYetRegisteredAsRunning_MarksItCancelled()
    {
        JobStore store = CreateStore();
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        store.AddOrUpdate(job);

        await store.CancelAsync(job.Id);

        GenerationJob? updated = store.TryGet(job.Id);
        Assert.NotNull(updated);
        Assert.Equal(JobStates.Cancelled, updated.State);
        Assert.NotNull(updated.EndTime);
    }

    [Fact]
    public async Task CancelAsync_UnknownJobId_ReturnsFalse()
    {
        JobStore store = CreateStore();

        Assert.False(await store.CancelAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task CancelAsync_KnownJob_ReturnsTrue()
    {
        JobStore store = CreateStore();
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        store.AddOrUpdate(job);

        Assert.True(await store.CancelAsync(job.Id));
    }

    [Fact]
    public async Task CancelAsync_JobAlreadyTerminalAndNotRegisteredAsRunning_LeavesItUntouched()
    {
        // Only a Pending job with no registered run gets marked Cancelled directly - a job that's
        // already finished (Complete/Failed/etc.) and was unregistered by the worker must not be
        // flipped back to Cancelled by a late cancel request racing the job's own completion.
        JobStore store = CreateStore();
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob() with
        {
            State = JobStates.Complete,
            EndTime = DateTimeOffset.UtcNow,
        };
        store.AddOrUpdate(job);

        // Still "found" - cancelling a finished job is a harmless no-op, not a 404.
        Assert.True(await store.CancelAsync(job.Id));

        Assert.Equal(JobStates.Complete, store.TryGet(job.Id)?.State);
    }

    [Fact]
    public async Task CancelAsync_JobCompletedButRunStillRegistered_LeavesItComplete()
    {
        // The worker sets Complete, then awaits the APNs push, and only unregisters the run in its
        // `finally` - so for that window the run is still registered on a finished job. A cancel
        // landing there must not flip it to Cancelled after the user was already told it's ready.
        JobStore store = CreateStore();
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        store.AddOrUpdate(job);

        // The workflow engine gives us no public way to construct a StreamingRun, so run a trivial one.
        Workflow workflow = new WorkflowBuilder(new Func<string, string>(s => s).BindAsExecutor("noop")).Build();
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
            workflow, "input", cancellationToken: TestContext.Current.CancellationToken);
        store.RegisterJobRun(job.Id, run);

        store.UpdateJob(job.Id, current => current with { State = JobStates.Complete, EndTime = DateTimeOffset.UtcNow });

        Assert.True(await store.CancelAsync(job.Id));

        Assert.Equal(JobStates.Complete, store.TryGet(job.Id)?.State);
    }

    [Fact]
    public void UpdateJob_ConcurrentWithAppendJobFeedback_LosesNeitherWrite()
    {
        // Regression for a race in GenerationJobWorkerHostedService: a `TryGet` snapshot followed by
        // `AddOrUpdate(snapshot with { ... })` can land between two `AppendJobFeedback` calls and
        // silently discard whichever one raced with it, since the overwrite doesn't merge - it just
        // replaces. `UpdateJob` closes that window by reading-and-writing as one atomic operation.
        JobStore store = CreateStore();
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        store.AddOrUpdate(job);

        Parallel.Invoke(
            () => store.AppendJobFeedback(job.Id, new JobFeedback(DateTime.UtcNow, "Retrieval", "Retrieving...", 1)),
            () => store.UpdateJob(job.Id, current => current with { State = JobStates.Complete, EndTime = DateTimeOffset.UtcNow }),
            () => store.AppendJobFeedback(job.Id, new JobFeedback(DateTime.UtcNow, "Persistence", "Saving...", 1)));

        GenerationJob? result = store.TryGet(job.Id);
        Assert.NotNull(result);
        Assert.Equal(JobStates.Complete, result.State);
        Assert.Equal(2, result.FeedbackItems.Count);
    }

    [Fact]
    public async Task CancelAsync_ConcurrentWithAppendJobFeedback_LosesNeitherWrite()
    {
        // CancelAsync used to TryGet a snapshot then AddOrUpdate it with State = Cancelled, which
        // could discard feedback appended in between. It now goes through UpdateJob.
        for (int i = 0; i < 200; i++)
        {
            JobStore store = CreateStore();
            GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
            store.AddOrUpdate(job);

            Task cancel = Task.Run(() => store.CancelAsync(job.Id), TestContext.Current.CancellationToken);
            Task append = Task.Run(() => store.AppendJobFeedback(job.Id, new JobFeedback(DateTime.UtcNow, "Retrieval", "Retrieving...", 1)), TestContext.Current.CancellationToken);
            await Task.WhenAll(cancel, append);

            GenerationJob? result = store.TryGet(job.Id);
            Assert.NotNull(result);
            Assert.Equal(JobStates.Cancelled, result.State);
            Assert.Single(result.FeedbackItems);
        }
    }

    [Fact]
    public void TryStartJob_PendingJob_MovesItToInProgress()
    {
        JobStore store = CreateStore();
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        store.AddOrUpdate(job);

        bool started = store.TryStartJob(job.Id, out GenerationJob? startedJob);

        Assert.True(started);
        Assert.Equal(JobStates.InProgress, startedJob?.State);
        Assert.Equal(JobStates.InProgress, store.TryGet(job.Id)?.State);
    }

    [Fact]
    public async Task TryStartJob_JobCancelledBeforeStart_LeavesItCancelled()
    {
        JobStore store = CreateStore();
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        store.AddOrUpdate(job);
        await store.CancelAsync(job.Id);

        bool started = store.TryStartJob(job.Id, out GenerationJob? startedJob);

        Assert.False(started);
        Assert.Null(startedJob);
        Assert.Equal(JobStates.Cancelled, store.TryGet(job.Id)?.State);
    }

    [Fact]
    public void TryStartJob_UnknownJobId_ReturnsFalse()
    {
        JobStore store = CreateStore();

        Assert.False(store.TryStartJob(Guid.NewGuid(), out _));
    }

    [Fact]
    public void TryStartJob_ConcurrentWithAppendJobFeedback_LosesNeitherWrite()
    {
        for (int i = 0; i < 200; i++)
        {
            JobStore store = CreateStore();
            GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
            store.AddOrUpdate(job);

            Parallel.Invoke(
                () => store.AppendJobFeedback(job.Id, new JobFeedback(DateTime.UtcNow, "Retrieval", "Retrieving...", 1)),
                () => store.TryStartJob(job.Id, out _));

            GenerationJob? result = store.TryGet(job.Id);
            Assert.Equal(JobStates.InProgress, result?.State);
            Assert.Single(result!.FeedbackItems);
        }
    }
}