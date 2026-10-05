using System.Diagnostics.Metrics;

namespace BackingTrack.Generation.Jobs;

public class JobStoreMetrics : IDisposable
{
    private const string MeterName = GenerationJobMetrics.MeterName;

    private readonly Meter _meter;
    private readonly ObservableGauge<int> _queueDepthGauge;
    private readonly ObservableGauge<int> _jobGauge;
    private readonly Counter<long> _evictedJobsCount;

    public JobStoreMetrics(JobStore jobStore, GenerationJobQueue queue)
    {
        JobStore jobStore1 = jobStore;
        GenerationJobQueue jobQueue = queue;

        _meter = new Meter(MeterName);
        _queueDepthGauge = _meter.CreateObservableGauge("generation_queue.depth",
            () => jobQueue.Reader.Count);

        _jobGauge = _meter.CreateObservableGauge("generation_job_store.jobs",
            () => jobStore1.CountByState()
                .Select(c => new Measurement<int>(c.Value, new KeyValuePair<string, object?>("job_state", c.Key.ToMetricTag()))),
            description: "Jobs currently held in the store, by state");

        _evictedJobsCount = _meter.CreateCounter<long>("generation_job.evicted",
            description: "Number of jobs evicted from the store");
    }

    public void RecordEvicted(int evictedCount)
    {
        if (evictedCount <= 0) return;
        _evictedJobsCount.Add(evictedCount);
    }

    public void Dispose() => _meter.Dispose();
}