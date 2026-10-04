using System.Threading.Channels;

using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Notifications;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.Retrieval;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackRepair;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Tests.Workflows;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace BackingTrack.Generation.Tests.Jobs;

/// <summary>
/// Drives the worker through the real workflow (real executors, substituted steps) and checks that
/// every terminal path records exactly one <c>generation_job.completed</c> measurement.
/// </summary>
[Collection(JobMetricsCollection.Name)]
public sealed class GenerationJobWorkerOutcomeMetricsTests : IDisposable
{
    private static readonly CandidateTrack Track = new(16, [], [], []);

    private readonly string _trackFolder = Directory.CreateTempSubdirectory("worker-outcome-tests").FullName;

    private readonly IRetrieveStep _retrieve = Substitute.For<IRetrieveStep>();
    private readonly IGenerateStep _generate = Substitute.For<IGenerateStep>();
    private readonly IValidateStep _validate = Substitute.For<IValidateStep>();
    private readonly ICriticStep _critic = Substitute.For<ICriticStep>();
    private readonly IFallbackGenerationStep _fallback = Substitute.For<IFallbackGenerationStep>();
    private readonly IRepairStep _repair = Substitute.For<IRepairStep>();

    private readonly JobStore _store = new(NullLogger<JobStore>.Instance);
    private readonly GenerationJobQueue _queue = new(Channel.CreateUnbounded<Guid>());
    private readonly GenerationJobMetrics _metrics = new();
    private readonly MeasurementCapture _capture = new(MeasurementCapture.JobCompletedInstrument);

    private int _generationMaxAttempts = 3;

    public GenerationJobWorkerOutcomeMetricsTests()
    {
        // A pipeline that succeeds end to end; each test overrides the one step it wants to break.
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        _retrieve.RetrieveTopMatchingStyles(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(WorkflowTestFixtures.CreateMatchedStyle());
        _retrieve.RetrieveMatchingChords(Arg.Any<IReadOnlyList<string>>()).Returns([chord]);
        _retrieve.RetrieveMatchingScales(Arg.Any<string>(), Arg.Any<KeyMode>(), Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<CancellationToken>())
            .Returns([WorkflowTestFixtures.CreateMatchedScale(chord)]);

        _generate.GenerateCandidateTrackAsync(default!, default, default, default, default, default!, default!, default!,
                Arg.Any<CandidateTrack?>(), Arg.Any<IReadOnlyList<ValidationFailure>?>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(Track);
        _validate.ValidateCandidateTrack(default!, default!, default!, default!).ReturnsForAnyArgs(Valid());
        _critic.ReviewCandidateTrackAsync(default!, default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(new CriticFinalReview(true, 100, []));
        _fallback.GenerateFallbackCandidateTrackAsync(default, default, default!, default!, default)
            .ReturnsForAnyArgs(Track);
    }

    public void Dispose()
    {
        _capture.Dispose();
        _metrics.Dispose();
        if (Directory.Exists(_trackFolder))
            Directory.Delete(_trackFolder, recursive: true);
    }

    [Fact]
    public async Task Job_completing_normally_records_complete()
    {
        GenerationJob job = await RunToTerminalStateAsync();

        Assert.Equal(JobStates.Complete, _store.TryGet(job.Id)?.State);
        AssertSingleOutcome("complete", "precise");
    }

    [Fact]
    public async Task Job_completing_via_fallback_records_completed_via_fallback()
    {
        // Attempt 1 fails validation and hits the cap, so the workflow falls back; the fallback track then validates.
        _generationMaxAttempts = 1;
        _validate.ValidateCandidateTrack(default!, default!, default!, default!)
            .ReturnsForAnyArgs(Invalid(), Valid());

        GenerationJob job = await RunToTerminalStateAsync();

        Assert.Equal(JobStates.CompletedViaFallback, _store.TryGet(job.Id)?.State);
        AssertSingleOutcome("completed_via_fallback", "precise");
    }

    [Fact]
    public async Task Job_failing_inside_the_workflow_records_failed()
    {
        _retrieve.RetrieveTopMatchingStyles(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<MatchedStyle?>>(_ => throw new InvalidOperationException("retrieval down"));

        GenerationJob job = await RunToTerminalStateAsync();

        Assert.Equal(JobStates.Failed, _store.TryGet(job.Id)?.State);
        AssertSingleOutcome("failed", "precise");
    }

    [Fact]
    public async Task Job_failing_with_no_matching_style_records_failed()
    {
        _retrieve.RetrieveTopMatchingStyles(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MatchedStyle?)null);

        GenerationJob job = await RunToTerminalStateAsync();

        Assert.Equal(JobStates.Failed, _store.TryGet(job.Id)?.State);
        AssertSingleOutcome("failed", "precise");
    }

    [Fact]
    public async Task Job_cancelled_while_running_records_cancelled_once()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _retrieve.RetrieveTopMatchingStyles(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<MatchedStyle?>>(async call =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return null;
            });

        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        _store.AddOrUpdate(job);
        await _queue.Writer.WriteAsync(job.Id, TestContext.Current.CancellationToken);

        using ServiceProvider provider = BuildProvider();
        await RunWorkerAsync(CreateWorker(provider), async () =>
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            // The run is registered with the store just after the engine starts, so a cancel that lands
            // before then only marks nothing; keep asking until it takes.
            await WaitUntilAsync(async () =>
            {
                await _store.CancelAsync(job.Id);
                return _store.TryGet(job.Id)?.State == JobStates.Cancelled;
            });
            await WaitUntilAsync(() => _capture.Measurements.Count > 0);
            await SettleAsync();

            // The cancel path marks the job Cancelled directly and the executor also yields
            // GenerationCancelled; the job must still be counted once, and never as failed.
            AssertSingleOutcome("cancelled", "precise");
        });
    }

    [Fact]
    public async Task Exception_escaping_the_worker_records_failed_once_with_the_jobs_mode()
    {
        // Scope creation is the first thing after the job starts, so throwing there stands in for
        // any unexpected failure that reaches the worker's catch-all.
        IServiceScopeFactory throwingScopeFactory = Substitute.For<IServiceScopeFactory>();
        throwingScopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("boom"));

        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob() with { Mode = GenerationMode.Vibe };
        _store.AddOrUpdate(job);
        await _queue.Writer.WriteAsync(job.Id, TestContext.Current.CancellationToken);

        GenerationJobWorkerHostedService worker = new(_store, _queue, NullLogger<GenerationJobWorkerHostedService>.Instance, _metrics, throwingScopeFactory);
        await RunWorkerAsync(worker, async () =>
        {
            await WaitUntilAsync(() => _capture.Measurements.Count > 0);
            await SettleAsync();

            AssertSingleOutcome("failed", "vibe");
        });
    }

    [Fact]
    public async Task Exception_after_the_job_reached_a_terminal_state_does_not_count_it_again()
    {
        // Completing normally, then the push notification throws: the catch-all must leave the
        // terminal state alone (markedFailed is false), so it must not record a second outcome.
        GenerationJob job = await RunToTerminalStateAsync(push: new FakePushEmitter { Throws = true });

        Assert.Equal(JobStates.Complete, _store.TryGet(job.Id)?.State);
        AssertSingleOutcome("complete", "precise");
    }

    [Fact]
    public async Task Failed_job_is_still_recorded_when_the_push_notification_throws()
    {
        _retrieve.RetrieveTopMatchingStyles(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<MatchedStyle?>>(_ => throw new InvalidOperationException("retrieval down"));

        GenerationJob job = await RunToTerminalStateAsync(push: new FakePushEmitter { Throws = true });

        Assert.Equal(JobStates.Failed, _store.TryGet(job.Id)?.State);
        AssertSingleOutcome("failed", "precise");
    }

    [Fact]
    public async Task Job_that_was_cancelled_before_it_started_is_not_recorded_by_the_worker()
    {
        // TODO: revisit once it's decided where pending-cancelled jobs are counted. The worker skips them
        // at TryStartJob; if they're counted at the cancel site instead, the worker must stay silent.
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        _store.AddOrUpdate(job);
        await _store.CancelAsync(job.Id);
        await _queue.Writer.WriteAsync(job.Id, TestContext.Current.CancellationToken);

        // A second, normal job lets us know the worker has got past the cancelled one.
        GenerationJob next = WorkflowTestFixtures.CreateGenerationJob();
        _store.AddOrUpdate(next);
        await _queue.Writer.WriteAsync(next.Id, TestContext.Current.CancellationToken);

        using ServiceProvider provider = BuildProvider();
        await RunWorkerAsync(CreateWorker(provider), async () =>
        {
            await WaitUntilAsync(() => _store.TryGet(next.Id)?.State == JobStates.Complete);
            await SettleAsync();

            AssertSingleOutcome("complete", "precise");
        });
    }

    private async Task<GenerationJob> RunToTerminalStateAsync(IPushNotificationEmitter? push = null)
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        _store.AddOrUpdate(job);
        await _queue.Writer.WriteAsync(job.Id, TestContext.Current.CancellationToken);

        using ServiceProvider provider = BuildProvider(push);
        await RunWorkerAsync(CreateWorker(provider), async () =>
        {
            await WaitUntilAsync(() => _capture.Measurements.Count > 0);
            await SettleAsync();
        });

        return job;
    }

    private static async Task RunWorkerAsync(GenerationJobWorkerHostedService worker, Func<Task> whileRunning)
    {
        await worker.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await whileRunning();
        }
        finally
        {
            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    private GenerationJobWorkerHostedService CreateWorker(ServiceProvider provider) =>
        new(_store, _queue, NullLogger<GenerationJobWorkerHostedService>.Instance, _metrics,
            provider.GetRequiredService<IServiceScopeFactory>());

    private ServiceProvider BuildProvider(IPushNotificationEmitter? push = null)
    {
        ServiceCollection services = new();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddTrackGenerationWorkflow();

        services.AddSingleton(_retrieve);
        services.AddSingleton(_generate);
        services.AddSingleton(_validate);
        services.AddSingleton(_critic);
        services.AddSingleton(_fallback);
        services.AddSingleton(_repair);
        services.AddSingleton(Substitute.For<IJobStatusEmitter>());
        services.AddSingleton(push ?? new FakePushEmitter());

        services.AddSingleton(Options.Create(new TrackStorageSettings { BaseFolderPath = _trackFolder }));
        services.AddSingleton(Options.Create(new CriticSettings { PassingScoreThreshold = 70, MaxAttempts = 1 }));
        services.AddSingleton(Options.Create(new TrackGenerationWorkflowSettings { GenerationMaxAttempts = _generationMaxAttempts }));

        return services.BuildServiceProvider();
    }

    private void AssertSingleOutcome(string outcome, string mode)
    {
        CapturedMeasurement measurement = Assert.Single(_capture.Measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal(outcome, measurement.Tags["outcome"]);
        Assert.Equal(mode, measurement.Tags["mode"]);
    }

    // Hand-written because the interface is internal, which NSubstitute's proxy generator can't reach.
    private sealed class FakePushEmitter : IPushNotificationEmitter
    {
        public bool Throws { get; init; }

        public Task SendApnsPushNotificationAsync(GenerationJob job) =>
            Throws ? throw new HttpRequestException("apns down") : Task.CompletedTask;
    }

    private static TrackValidationResult Valid() => new(true, []);

    private static TrackValidationResult Invalid() =>
        new(false, [new ValidationFailure(ValidationFailureType.PartEmpty, null, "empty")]);

    // The measurement is recorded as the worker finishes the job, so a duplicate would land shortly
    // after the first; give it a moment so "exactly one" isn't just "one so far".
    private static Task SettleAsync() => Task.Delay(250, TestContext.Current.CancellationToken);

    private static Task WaitUntilAsync(Func<bool> condition) => WaitUntilAsync(() => Task.FromResult(condition()));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!await condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}