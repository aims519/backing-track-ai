using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BackingTrack.Generation.Jobs;

public class GenerationJobMetrics : IDisposable
{
    public const string MeterName = "BackingTrack.GenerationJob";

    private readonly Meter _meter;
    private readonly Histogram<long> _attempts;
    private readonly Counter<long> _attemptsUsed;
    private readonly Counter<long> _completedJobs;

    public GenerationJobMetrics()
    {
        _meter = new Meter(MeterName);
        _attempts = _meter.CreateHistogram<long>("generation_job.attempts",
            description: "Number of validate/repair attempts used by a job before it succeeded, exhausted retries, or errored, tagged by outcome");

        // A separate counter tagged by the exact attempt value: attempts is a small, bounded
        // integer, and the histogram's default bucket boundaries (tuned for millisecond-scale
        // latencies) collapse every value 1..N into a single bucket, making it useless for
        // charting the actual distribution over time.
        _attemptsUsed = _meter.CreateCounter<long>("generation_job.attempts_used",
            description: "Count of jobs, tagged by the exact number of attempts used and outcome");

        _completedJobs = _meter.CreateCounter<long>("generation_job.completed",
            description: "Count of jobs reaching a terminal state, tagged by outcome and generation mode");
    }

    public void RecordAttempts(long attempts, string outcome)
    {
        _attempts.Record(attempts, new TagList { { "outcome", outcome } });
        _attemptsUsed.Add(1, new TagList { { "attempts", attempts }, { "outcome", outcome } });
    }

    // Counts every job exactly once. attempts_used is only recorded when an attempt count exists and uses
    // its own success/failed outcome values, so its total won't match this one.
    public void RecordOutcome(JobStates state, GenerationMode mode)
    {
        // Explicit strings instead of state.ToString(): tag values are a dashboard contract
        string outcome = state switch
        {
            JobStates.Complete => "complete",
            JobStates.Cancelled => "cancelled",
            JobStates.CompletedViaFallback => "completed_via_fallback",
            JobStates.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Only jobs in a terminal state can be recorded")
        };

        _completedJobs.Add(1, new TagList { { "outcome", outcome }, { "mode", mode.ToString().ToLowerInvariant() } });
    }

    public void Dispose() => _meter.Dispose();
}