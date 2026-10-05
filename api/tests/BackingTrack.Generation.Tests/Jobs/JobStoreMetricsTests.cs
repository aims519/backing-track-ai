using System.Threading.Channels;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Tests.Workflows;

using Microsoft.Extensions.Logging.Abstractions;

namespace BackingTrack.Generation.Tests.Jobs;

[Collection(JobMetricsCollection.Name)]
public class JobStoreMetricsTests
{
    private const string QueueDepthInstrument = "generation_queue.depth";
    private const string StoreJobsInstrument = "generation_job_store.jobs";
    private const string EvictedInstrument = "generation_job.evicted";

    private static (JobStore Store, Channel<Guid> Channel, JobStoreMetrics Metrics) Create()
    {
        JobStore store = new(NullLogger<JobStore>.Instance);
        Channel<Guid> channel = Channel.CreateUnbounded<Guid>();
        return (store, channel, new JobStoreMetrics(store, new GenerationJobQueue(channel)));
    }

    [Fact]
    public void QueueDepth_ReportsHowManyJobsAreWaitingInTheQueue()
    {
        (JobStore _, Channel<Guid> channel, JobStoreMetrics metrics) = Create();
        using JobStoreMetrics _ = metrics;
        using MeasurementCapture capture = new(QueueDepthInstrument);
        channel.Writer.TryWrite(Guid.NewGuid());
        channel.Writer.TryWrite(Guid.NewGuid());

        capture.Poll();

        Assert.Equal(2, Assert.Single(capture.Measurements).Value);
    }

    [Fact]
    public void QueueDepth_EmptyQueue_ReportsZero()
    {
        using JobStoreMetrics metrics = Create().Metrics;
        using MeasurementCapture capture = new(QueueDepthInstrument);

        capture.Poll();

        Assert.Equal(0, Assert.Single(capture.Measurements).Value);
    }

    [Fact]
    public void QueueDepth_FallsAsTheWorkerReadsJobs()
    {
        (JobStore _, Channel<Guid> channel, JobStoreMetrics metrics) = Create();
        using JobStoreMetrics _ = metrics;
        using MeasurementCapture capture = new(QueueDepthInstrument);
        channel.Writer.TryWrite(Guid.NewGuid());
        channel.Reader.TryRead(out Guid _);

        capture.Poll();

        Assert.Equal(0, Assert.Single(capture.Measurements).Value);
    }

    [Fact]
    public void StoreJobs_ReportsOneMeasurementPerStateIncludingZeros()
    {
        (JobStore store, Channel<Guid> _, JobStoreMetrics metrics) = Create();
        using JobStoreMetrics _ = metrics;
        using MeasurementCapture capture = new(StoreJobsInstrument);
        store.AddOrUpdate(WorkflowTestFixtures.CreateGenerationJob() with { State = JobStates.Pending });
        store.AddOrUpdate(WorkflowTestFixtures.CreateGenerationJob() with { State = JobStates.Pending });
        store.AddOrUpdate(WorkflowTestFixtures.CreateGenerationJob() with { State = JobStates.Failed });

        capture.Poll();

        Dictionary<string, double> byState = capture.Measurements.ToDictionary(m => (string)m.Tags["state"]!, m => m.Value);
        Assert.Equal(Enum.GetValues<JobStates>().Length, byState.Count);
        Assert.Equal(2, byState["pending"]);
        Assert.Equal(1, byState["failed"]);
        Assert.Equal(0, byState["in_progress"]);
        Assert.Equal(0, byState["complete"]);
        Assert.Equal(0, byState["completed_via_fallback"]);
        Assert.Equal(0, byState["cancelled"]);
    }

    [Fact]
    public void RecordEvicted_AddsTheNumberOfJobsEvicted()
    {
        using JobStoreMetrics metrics = Create().Metrics;
        using MeasurementCapture capture = new(EvictedInstrument);

        metrics.RecordEvicted(3);

        Assert.Equal(3, Assert.Single(capture.Measurements).Value);
    }

    [Fact]
    public void RecordEvicted_NothingEvicted_RecordsNothing()
    {
        using JobStoreMetrics metrics = Create().Metrics;
        using MeasurementCapture capture = new(EvictedInstrument);

        metrics.RecordEvicted(0);

        Assert.Empty(capture.Measurements);
    }
}