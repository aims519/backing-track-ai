using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Tests.Workflows;

using Microsoft.Extensions.Logging.Abstractions;

namespace BackingTrack.Generation.Tests.Jobs;

public class JobStoreEvictionTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static JobStore CreateStore() => new(NullLogger<JobStore>.Instance);

    private static GenerationJob AddJob(JobStore store, JobStates state, DateTimeOffset? endTime)
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob() with { State = state, EndTime = endTime };
        store.AddOrUpdate(job);
        return job;
    }

    [Theory]
    [InlineData(JobStates.Complete)]
    [InlineData(JobStates.CompletedViaFallback)]
    [InlineData(JobStates.Failed)]
    [InlineData(JobStates.Cancelled)]
    public void EvictTerminatedJobs_TerminalJobFinishedBeforeCutoff_RemovesIt(JobStates state)
    {
        JobStore store = CreateStore();
        GenerationJob job = AddJob(store, state, Cutoff.AddMinutes(-1));

        store.EvictTerminatedJobs(Cutoff);

        Assert.Null(store.TryGet(job.Id));
    }

    [Theory]
    [InlineData(JobStates.Complete)]
    [InlineData(JobStates.CompletedViaFallback)]
    [InlineData(JobStates.Failed)]
    [InlineData(JobStates.Cancelled)]
    public void EvictTerminatedJobs_TerminalJobFinishedAfterCutoff_KeepsIt(JobStates state)
    {
        JobStore store = CreateStore();
        GenerationJob job = AddJob(store, state, Cutoff.AddMinutes(1));

        store.EvictTerminatedJobs(Cutoff);

        Assert.NotNull(store.TryGet(job.Id));
    }

    [Fact]
    public void EvictTerminatedJobs_TerminalJobFinishedExactlyAtCutoff_KeepsIt()
    {
        // The comparison is strict (EndTime < cutoff), so a job is kept for the full TTL, not one tick less.
        JobStore store = CreateStore();
        GenerationJob job = AddJob(store, JobStates.Complete, Cutoff);

        store.EvictTerminatedJobs(Cutoff);

        Assert.NotNull(store.TryGet(job.Id));
    }

    [Theory]
    [InlineData(JobStates.Pending)]
    [InlineData(JobStates.InProgress)]
    public void EvictTerminatedJobs_JobStillRunning_KeepsItEvenWhenItsEndTimeIsOld(JobStates state)
    {
        // Pending/InProgress jobs normally have no EndTime; setting an old one proves eviction
        // keys off the state, not just the timestamp.
        JobStore store = CreateStore();
        GenerationJob withoutEndTime = AddJob(store, state, endTime: null);
        GenerationJob withOldEndTime = AddJob(store, state, Cutoff.AddDays(-1));

        store.EvictTerminatedJobs(Cutoff);

        Assert.NotNull(store.TryGet(withoutEndTime.Id));
        Assert.NotNull(store.TryGet(withOldEndTime.Id));
    }

    [Fact]
    public void EvictTerminatedJobs_TerminalJobWithoutEndTime_KeepsIt()
    {
        // Documents current behaviour: `null < cutoff` is false, so a terminal job that never got an
        // EndTime is never evicted. Every transition in the worker sets one today.
        JobStore store = CreateStore();
        GenerationJob job = AddJob(store, JobStates.Failed, endTime: null);

        store.EvictTerminatedJobs(Cutoff);

        Assert.NotNull(store.TryGet(job.Id));
    }

    [Fact]
    public void EvictTerminatedJobs_MixedJobs_RemovesOnlyTheExpiredTerminalOnes()
    {
        JobStore store = CreateStore();
        GenerationJob expiredComplete = AddJob(store, JobStates.Complete, Cutoff.AddHours(-2));
        GenerationJob expiredFailed = AddJob(store, JobStates.Failed, Cutoff.AddHours(-1));
        GenerationJob recentCancelled = AddJob(store, JobStates.Cancelled, Cutoff.AddMinutes(5));
        GenerationJob pending = AddJob(store, JobStates.Pending, endTime: null);
        GenerationJob inProgress = AddJob(store, JobStates.InProgress, endTime: null);

        store.EvictTerminatedJobs(Cutoff);

        Assert.Null(store.TryGet(expiredComplete.Id));
        Assert.Null(store.TryGet(expiredFailed.Id));
        Assert.NotNull(store.TryGet(recentCancelled.Id));
        Assert.NotNull(store.TryGet(pending.Id));
        Assert.NotNull(store.TryGet(inProgress.Id));
        Assert.Equal(3, store.JobCount);
    }

    [Fact]
    public void EvictTerminatedJobs_EmptyStore_DoesNothing()
    {
        JobStore store = CreateStore();

        store.EvictTerminatedJobs(Cutoff);

        Assert.Equal(0, store.JobCount);
    }

    [Fact]
    public void EvictTerminatedJobs_CalledTwice_IsIdempotent()
    {
        JobStore store = CreateStore();
        AddJob(store, JobStates.Complete, Cutoff.AddHours(-1));
        GenerationJob kept = AddJob(store, JobStates.Complete, Cutoff.AddHours(1));

        store.EvictTerminatedJobs(Cutoff);
        store.EvictTerminatedJobs(Cutoff);

        Assert.Equal(1, store.JobCount);
        Assert.NotNull(store.TryGet(kept.Id));
    }

    [Fact]
    public void EvictTerminatedJobs_ReturnsTheNumberOfJobsRemoved()
    {
        JobStore store = CreateStore();
        AddJob(store, JobStates.Complete, Cutoff.AddHours(-2));
        AddJob(store, JobStates.Failed, Cutoff.AddHours(-1));
        AddJob(store, JobStates.Cancelled, Cutoff.AddMinutes(5));
        AddJob(store, JobStates.InProgress, endTime: null);

        int evicted = store.EvictTerminatedJobs(Cutoff);

        Assert.Equal(2, evicted);
    }

    [Fact]
    public void EvictTerminatedJobs_NothingExpired_ReturnsZero()
    {
        JobStore store = CreateStore();
        AddJob(store, JobStates.Complete, Cutoff.AddMinutes(5));
        AddJob(store, JobStates.Pending, endTime: null);

        Assert.Equal(0, store.EvictTerminatedJobs(Cutoff));
    }

    [Fact]
    public void EvictTerminatedJobs_SecondCall_ReturnsZeroBecauseTheJobsAreAlreadyGone()
    {
        JobStore store = CreateStore();
        AddJob(store, JobStates.Complete, Cutoff.AddHours(-1));

        Assert.Equal(1, store.EvictTerminatedJobs(Cutoff));
        Assert.Equal(0, store.EvictTerminatedJobs(Cutoff));
    }

    [Fact]
    public async Task CancelAsync_JobAlreadyEvicted_ReturnsFalse()
    {
        // The client-visible consequence of eviction: cancelling an evicted job is a 404, same as an unknown id.
        JobStore store = CreateStore();
        GenerationJob job = AddJob(store, JobStates.Complete, Cutoff.AddHours(-1));
        store.EvictTerminatedJobs(Cutoff);

        Assert.False(await store.CancelAsync(job.Id));
    }
}