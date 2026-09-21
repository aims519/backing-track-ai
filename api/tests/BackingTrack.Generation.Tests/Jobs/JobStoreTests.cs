using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Tests.Workflows;

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
    public async Task CancelAsync_UnknownJobId_IsANoOp()
    {
        JobStore store = CreateStore();

        await store.CancelAsync(Guid.NewGuid());

        // Nothing to assert on the (nonexistent) job - this is really just confirming
        // CancelAsync doesn't throw for a job id it's never seen.
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

        await store.CancelAsync(job.Id);

        Assert.Equal(JobStates.Complete, store.TryGet(job.Id)?.State);
    }
}