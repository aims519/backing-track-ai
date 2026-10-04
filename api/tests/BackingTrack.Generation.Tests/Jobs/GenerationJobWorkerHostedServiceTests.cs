using System.Threading.Channels;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Notifications;
using BackingTrack.Generation.Tests.Workflows;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace BackingTrack.Generation.Tests.Jobs;

[Collection(JobMetricsCollection.Name)]
public class GenerationJobWorkerHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ExceptionEscapesProcessing_FailsTheJobAndKeepsProcessingTheQueue()
    {
        JobStore store = new(NullLogger<JobStore>.Instance);
        GenerationJobQueue queue = new(Channel.CreateUnbounded<Guid>());
        using GenerationJobMetrics metrics = new();

        // Scope creation is the first thing after the job is started, so throwing here stands in
        // for any unexpected failure that escapes the per-job handling.
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("boom"));

        GenerationJob first = WorkflowTestFixtures.CreateGenerationJob();
        GenerationJob second = WorkflowTestFixtures.CreateGenerationJob();
        store.AddOrUpdate(first);
        store.AddOrUpdate(second);
        await queue.Writer.WriteAsync(first.Id, TestContext.Current.CancellationToken);
        await queue.Writer.WriteAsync(second.Id, TestContext.Current.CancellationToken);

        GenerationJobWorkerHostedService worker = new(store, queue, NullLogger<GenerationJobWorkerHostedService>.Instance, metrics, scopeFactory);
        await worker.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            // The second job only reaches Failed if the loop survived the first job's exception.
            await WaitUntilAsync(() => store.TryGet(second.Id)?.State == JobStates.Failed);

            Assert.Equal(JobStates.Failed, store.TryGet(first.Id)?.State);
            Assert.NotNull(store.TryGet(first.Id)?.EndTime);
            Assert.False(worker.ExecuteTask?.IsFaulted);
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_ExceptionEscapesProcessing_SendsOneBestEffortFailurePush(bool pushThrows)
    {
        JobStore store = new(NullLogger<JobStore>.Instance);
        GenerationJobQueue queue = new(Channel.CreateUnbounded<Guid>());
        using GenerationJobMetrics metrics = new();
        RecordingPushEmitter emitter = new() { Throws = pushThrows };

        IServiceProvider provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IPushNotificationEmitter)).Returns(emitter);
        IServiceScope pushScope = Substitute.For<IServiceScope>();
        pushScope.ServiceProvider.Returns(provider);

        // First scope (job processing) throws; the second, fresh one is the push's.
        IServiceScopeFactory scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("boom"), _ => pushScope);

        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        store.AddOrUpdate(job);
        await queue.Writer.WriteAsync(job.Id, TestContext.Current.CancellationToken);

        GenerationJobWorkerHostedService worker = new(store, queue, NullLogger<GenerationJobWorkerHostedService>.Instance, metrics, scopeFactory);
        await worker.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            await WaitUntilAsync(() => emitter.Sent.Count > 0);

            GenerationJob pushed = Assert.Single(emitter.Sent);
            Assert.Equal(JobStates.Failed, pushed.State);
            Assert.False(worker.ExecuteTask?.IsFaulted);
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private sealed class RecordingPushEmitter : IPushNotificationEmitter
    {
        public bool Throws { get; init; }
        public List<GenerationJob> Sent { get; } = [];

        public Task SendApnsPushNotificationAsync(GenerationJob job)
        {
            Sent.Add(job);
            return Throws ? throw new HttpRequestException("apns down") : Task.CompletedTask;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}