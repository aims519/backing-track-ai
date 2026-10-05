using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Notifications;
using BackingTrack.Generation.Tests.Jobs;

namespace BackingTrack.Generation.Tests.Notifications;

[Collection(PushNotificationMetricsCollection.Name)]
public class PushNotificationMetricsTests
{
    private const string AttemptsInstrument = "push_notification.attempts";
    private const string DurationInstrument = "push_notification.duration";

    private static MeasurementCapture Capture(string instrument) => new(instrument, PushNotificationMetrics.MeterName);

    [Fact]
    public void RecordSent_CountsAnAttemptTaggedWithOutcomeAndJobState()
    {
        using PushNotificationMetrics metrics = new();
        using MeasurementCapture attempts = Capture(AttemptsInstrument);

        metrics.RecordSent(JobStates.CompletedViaFallback, TimeSpan.FromSeconds(1));

        CapturedMeasurement attempt = Assert.Single(attempts.Measurements);
        Assert.Equal(1, attempt.Value);
        Assert.Equal("sent", attempt.Tags["outcome"]);
        Assert.Equal("completed_via_fallback", attempt.Tags["job_state"]);
        Assert.False(attempt.Tags.ContainsKey("status_code"));
        Assert.False(attempt.Tags.ContainsKey("reason"));
    }

    [Fact]
    public void RecordRejected_TagsTheStatusCodeAndApnsReason()
    {
        using PushNotificationMetrics metrics = new();
        using MeasurementCapture attempts = Capture(AttemptsInstrument);

        metrics.RecordRejected(JobStates.Complete, 410, "Unregistered", TimeSpan.FromSeconds(1));

        CapturedMeasurement attempt = Assert.Single(attempts.Measurements);
        Assert.Equal("rejected", attempt.Tags["outcome"]);
        Assert.Equal("complete", attempt.Tags["job_state"]);
        Assert.Equal("410", attempt.Tags["status_code"]);
        Assert.Equal("Unregistered", attempt.Tags["reason"]);
    }

    [Fact]
    public void RecordRejected_NoReason_TagsUnknown()
    {
        using PushNotificationMetrics metrics = new();
        using MeasurementCapture attempts = Capture(AttemptsInstrument);

        metrics.RecordRejected(JobStates.Complete, 503, null, TimeSpan.FromSeconds(1));

        Assert.Equal("unknown", Assert.Single(attempts.Measurements).Tags["reason"]);
    }

    [Fact]
    public void RecordTimeout_TagsTimeoutOutcome()
    {
        using PushNotificationMetrics metrics = new();
        using MeasurementCapture attempts = Capture(AttemptsInstrument);

        metrics.RecordTimeout(JobStates.Failed, TimeSpan.FromSeconds(10));

        CapturedMeasurement attempt = Assert.Single(attempts.Measurements);
        Assert.Equal("timeout", attempt.Tags["outcome"]);
        Assert.Equal("failed", attempt.Tags["job_state"]);
    }

    [Fact]
    public void RecordError_TagsErrorOutcome()
    {
        using PushNotificationMetrics metrics = new();
        using MeasurementCapture attempts = Capture(AttemptsInstrument);

        metrics.RecordError(JobStates.Complete, TimeSpan.FromSeconds(1));

        Assert.Equal("error", Assert.Single(attempts.Measurements).Tags["outcome"]);
    }

    [Fact]
    public void EveryOutcome_RecordsTheDurationInSecondsTaggedWithOutcome()
    {
        using PushNotificationMetrics metrics = new();
        using MeasurementCapture duration = Capture(DurationInstrument);

        metrics.RecordSent(JobStates.Complete, TimeSpan.FromMilliseconds(1500));
        metrics.RecordRejected(JobStates.Complete, 400, "BadDeviceToken", TimeSpan.FromSeconds(2));
        metrics.RecordTimeout(JobStates.Complete, TimeSpan.FromSeconds(10));
        metrics.RecordError(JobStates.Complete, TimeSpan.FromSeconds(3));

        Assert.Equal([1.5, 2, 10, 3], duration.Measurements.Select(m => m.Value));
        Assert.Equal(["sent", "rejected", "timeout", "error"], duration.Measurements.Select(m => (string)m.Tags["outcome"]!));
    }
}