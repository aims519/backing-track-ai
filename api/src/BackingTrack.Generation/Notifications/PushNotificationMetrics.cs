using System.Diagnostics;
using System.Diagnostics.Metrics;

using BackingTrack.Generation.Jobs;

namespace BackingTrack.Generation.Notifications;

public class PushNotificationMetrics : IDisposable
{
    public const string MeterName = "BackingTrack.PushNotification";

    private readonly Meter _meter;
    private readonly Histogram<double> _durations;
    private readonly Counter<long> _attempts;

    public PushNotificationMetrics()
    {
        _meter = new Meter(MeterName);

        _attempts = _meter.CreateCounter<long>("push_notification.attempts",
            description: "Push notification attempt count, tagged by push outcome and job state");

        _durations = _meter.CreateHistogram<double>("push_notification.duration",
            unit: "s",
            description: "Durations of push notifications in seconds, tagged by job outcome");
    }

    public void RecordSent(JobStates state, TimeSpan duration)
    {
        TagList tags = new() { { "job_state", state.ToMetricTag() }, { "outcome", "sent" } };

        _attempts.Add(1, tags);
        _durations.Record(duration.TotalSeconds, tags);
    }

    public void RecordError(JobStates state, TimeSpan duration)
    {
        TagList tags = new() { { "job_state", state.ToMetricTag() }, { "outcome", "error" } };

        _attempts.Add(1, tags);
        _durations.Record(duration.TotalSeconds, tags);
    }

    public void RecordRejected(JobStates state, int statusCode, string? reason, TimeSpan duration)
    {
        TagList tags = new()
        {
            { "job_state", state.ToMetricTag() },
            { "outcome", "rejected" },
            { "status_code", statusCode.ToString() },
            { "reason", reason is null || reason.Length > 50 ? "unknown" : reason },
        };

        _attempts.Add(1, tags);
        _durations.Record(duration.TotalSeconds, tags);
    }

    public void RecordTimeout(JobStates state, TimeSpan duration)
    {
        TagList tags = new() { { "job_state", state.ToMetricTag() }, { "outcome", "timeout" } };

        _attempts.Add(1, tags);
        _durations.Record(duration.TotalSeconds, tags);
    }

    public void Dispose() => _meter.Dispose();
}