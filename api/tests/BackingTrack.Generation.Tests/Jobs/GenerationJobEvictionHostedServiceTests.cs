using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Tests.Workflows;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BackingTrack.Generation.Tests.Jobs;

public class GenerationJobEvictionHostedServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static GenerationJobEvictionHostedService CreateService(JobStore store, FakeTimeProvider time, int ttlSeconds, int sweepIntervalSeconds = 3600) =>
        new(store, time,
            Options.Create(new JobEvictionSettings { SweepIntervalSeconds = sweepIntervalSeconds, JobTtlSeconds = ttlSeconds }),
            NullLogger<GenerationJobEvictionHostedService>.Instance);

    private static GenerationJob AddJob(JobStore store, JobStates state, DateTimeOffset? endTime)
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob() with { State = state, EndTime = endTime };
        store.AddOrUpdate(job);
        return job;
    }

    // The first sweep isn't guaranteed to have finished when StartAsync returns, so poll for its effect.
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task StartAsync_SweepsImmediatelyUsingTtlRelativeToTheClock()
    {
        FakeTimeProvider time = new(Now);
        JobStore store = new(NullLogger<JobStore>.Instance);
        GenerationJob expired = AddJob(store, JobStates.Complete, Now.AddSeconds(-61));
        GenerationJob withinTtl = AddJob(store, JobStates.Complete, Now.AddSeconds(-59));
        GenerationJob running = AddJob(store, JobStates.InProgress, endTime: null);
        GenerationJobEvictionHostedService service = CreateService(store, time, ttlSeconds: 60);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => store.TryGet(expired.Id) is null);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Null(store.TryGet(expired.Id));
        Assert.NotNull(store.TryGet(withinTtl.Id));
        Assert.NotNull(store.TryGet(running.Id));
    }

    [Fact]
    public async Task NextSweep_AfterTheIntervalElapses_EvictsJobsThatHaveSinceExpired()
    {
        FakeTimeProvider time = new(Now);
        JobStore store = new(NullLogger<JobStore>.Instance);
        // Finished "now", so the first sweep (TTL 60s) must leave it alone; the already-expired job is a
        // marker that the first sweep has run - and so that the timer exists - before the clock moves.
        GenerationJob finishedJustNow = AddJob(store, JobStates.Complete, Now);
        GenerationJob alreadyExpired = AddJob(store, JobStates.Complete, Now.AddSeconds(-120));
        GenerationJobEvictionHostedService service = CreateService(store, time, ttlSeconds: 60, sweepIntervalSeconds: 10);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => store.TryGet(alreadyExpired.Id) is null);
        Assert.NotNull(store.TryGet(finishedJustNow.Id));

        time.Advance(TimeSpan.FromSeconds(70));
        await WaitUntilAsync(() => store.TryGet(finishedJustNow.Id) is null);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Null(store.TryGet(finishedJustNow.Id));
    }

    [Fact]
    public async Task Sweep_ThatThrows_WaitsForTheNextTickInsteadOfRetryingImmediately()
    {
        // The sweep reads the clock inside its try block, so a clock that throws makes every sweep fail.
        // A loop that retried without waiting would call it thousands of times in the settle period below.
        ThrowingTimeProvider time = new(Now);
        JobStore store = new(NullLogger<JobStore>.Instance);
        GenerationJobEvictionHostedService service = CreateService(store, time, ttlSeconds: 60, sweepIntervalSeconds: 10);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => time.GetUtcNowCalls >= 1);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(1, time.GetUtcNowCalls);

        time.Advance(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => time.GetUtcNowCalls >= 2);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, time.GetUtcNowCalls);
    }

    private sealed class ThrowingTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private int _getUtcNowCalls;
        public int GetUtcNowCalls => Volatile.Read(ref _getUtcNowCalls);

        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Increment(ref _getUtcNowCalls);
            throw new InvalidOperationException("clock failure");
        }
    }

    [Fact]
    public async Task StopAsync_ShutsDownCleanlyWhileWaitingForTheNextSweep()
    {
        JobStore store = new(NullLogger<JobStore>.Instance);
        GenerationJobEvictionHostedService service = CreateService(store, new FakeTimeProvider(Now), ttlSeconds: 60);
        await service.StartAsync(TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(service.ExecuteTask?.IsCompleted);
    }
}