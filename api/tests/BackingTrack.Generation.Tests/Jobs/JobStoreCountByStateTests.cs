using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Tests.Workflows;

using Microsoft.Extensions.Logging.Abstractions;

namespace BackingTrack.Generation.Tests.Jobs;

public class JobStoreCountByStateTests
{
    private static JobStore CreateStore() => new(NullLogger<JobStore>.Instance);

    private static void AddJob(JobStore store, JobStates state) =>
        store.AddOrUpdate(WorkflowTestFixtures.CreateGenerationJob() with { State = state });

    [Fact]
    public void CountByState_EmptyStore_ReportsEveryStateAsZero()
    {
        // Every state is present, zeros included, so a gauge built from this drops back to 0 instead of going stale.
        IReadOnlyDictionary<JobStates, int> counts = CreateStore().CountByState();

        Assert.Equal(Enum.GetValues<JobStates>().Length, counts.Count);
        Assert.All(counts.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public void CountByState_MixedJobs_CountsEachStateSeparately()
    {
        JobStore store = CreateStore();
        AddJob(store, JobStates.Pending);
        AddJob(store, JobStates.Pending);
        AddJob(store, JobStates.InProgress);
        AddJob(store, JobStates.Failed);
        AddJob(store, JobStates.Complete);
        AddJob(store, JobStates.Complete);
        AddJob(store, JobStates.Complete);

        IReadOnlyDictionary<JobStates, int> counts = store.CountByState();

        Assert.Equal(2, counts[JobStates.Pending]);
        Assert.Equal(1, counts[JobStates.InProgress]);
        Assert.Equal(3, counts[JobStates.Complete]);
        Assert.Equal(1, counts[JobStates.Failed]);
        Assert.Equal(0, counts[JobStates.CompletedViaFallback]);
        Assert.Equal(0, counts[JobStates.Cancelled]);
    }

    [Fact]
    public void CountByState_JobChangesState_IsReflectedOnTheNextCall()
    {
        JobStore store = CreateStore();
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob() with { State = JobStates.Pending };
        store.AddOrUpdate(job);

        store.TryStartJob(job.Id, out _);

        IReadOnlyDictionary<JobStates, int> counts = store.CountByState();
        Assert.Equal(0, counts[JobStates.Pending]);
        Assert.Equal(1, counts[JobStates.InProgress]);
    }
}