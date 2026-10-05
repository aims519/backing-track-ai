using System.Diagnostics.Metrics;

using BackingTrack.Generation.Notifications;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackRepair;

namespace BackingTrack.Generation.Tests;

/// <summary>
/// The OpenTelemetry SDK's default histogram buckets (0, 5, 10, 25, 50, 75, 100, 250, ...) are tuned for
/// millisecond latencies. Our durations are in seconds, so without explicit boundaries a 0.6s push lands in the
/// first bucket and every percentile of it reads as ~5s. These check each seconds-based duration histogram declares
/// boundaries that fit what it measures. They pin the shape (ascending, covering the real range with some
/// resolution), not the exact values, so tuning them doesn't break the tests.
/// </summary>
public class DurationHistogramBucketsTests
{
    // The Flex tier can queue for a long time (the client allows 15 minutes), but a typical call is well under a minute.
    private const double LlmCallTimeoutSeconds = 900;

    // The APNs client's own timeout (see AddPushNotifications).
    private const double PushTimeoutSeconds = 10;

    public static TheoryData<string, string, Func<IDisposable>> LlmCallDurations => new()
    {
        { TrackGenerationMetrics.MeterName, "track_generation.call.duration", () => new TrackGenerationMetrics() },
        { TrackRepairMetrics.MeterName, "track_repair.call.duration", () => new TrackRepairMetrics() },
        { CriticMetrics.MeterName, "critic.call.duration", () => new CriticMetrics() },
    };

    private static IReadOnlyList<double> Boundaries(string meterName, string instrumentName, Func<IDisposable> create)
    {
        IReadOnlyList<double>? boundaries = null;
        bool found = false;

        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName && instrument is Histogram<double> histogram)
            {
                found = true;
                boundaries = histogram.Advice?.HistogramBucketBoundaries;
            }
        };
        listener.Start();

        using IDisposable metrics = create();

        Assert.True(found, $"{instrumentName} was not published as a Histogram<double>");
        Assert.NotNull(boundaries);
        return boundaries;
    }

    [Theory]
    [MemberData(nameof(LlmCallDurations))]
    public void LlmCallDuration_DeclaresBucketsThatSpanFastCallsToTheFlexTimeout(string meterName, string instrumentName, Func<IDisposable> create)
    {
        IReadOnlyList<double> boundaries = Boundaries(meterName, instrumentName, create);

        Assert.Equal(boundaries.Order(), boundaries);
        Assert.Equal(boundaries.Distinct().Count(), boundaries.Count);
        Assert.True(boundaries[0] <= 1, $"Smallest boundary {boundaries[0]}s should resolve a call that finishes in about a second");
        Assert.True(boundaries[^1] >= LlmCallTimeoutSeconds, $"Largest boundary {boundaries[^1]}s should reach the {LlmCallTimeoutSeconds}s timeout");
    }

    [Theory]
    [MemberData(nameof(LlmCallDurations))]
    public void LlmCallDuration_HasResolutionWhereCallsActuallyLand(string meterName, string instrumentName, Func<IDisposable> create)
    {
        // Typical calls take roughly 5s to a minute; the default buckets give only 5, 10, 25, 50 and 75 there.
        IReadOnlyList<double> boundaries = Boundaries(meterName, instrumentName, create);

        int inTypicalRange = boundaries.Count(b => b is >= 5 and <= 60);

        Assert.True(inTypicalRange >= 6, $"Expected at least 6 boundaries between 5s and 60s, found {inTypicalRange}");
    }

    [Fact]
    public void PushDuration_DeclaresBucketsThatSpanSubSecondToTheTimeout()
    {
        IReadOnlyList<double> boundaries = Boundaries(PushNotificationMetrics.MeterName, "push_notification.duration", () => new PushNotificationMetrics());

        Assert.Equal(boundaries.Order(), boundaries);
        Assert.Equal(boundaries.Distinct().Count(), boundaries.Count);
        Assert.True(boundaries[0] <= 0.1, $"Smallest boundary {boundaries[0]}s should resolve a push that takes a fraction of a second");
        Assert.True(boundaries[^1] >= PushTimeoutSeconds, $"Largest boundary {boundaries[^1]}s should reach the {PushTimeoutSeconds}s timeout");
    }

    [Fact]
    public void PushDuration_HasResolutionWhereAPushActuallyLands()
    {
        // A healthy push takes around 0.6s; the default buckets put everything under 5s in one bucket.
        IReadOnlyList<double> boundaries = Boundaries(PushNotificationMetrics.MeterName, "push_notification.duration", () => new PushNotificationMetrics());

        int belowTwoSeconds = boundaries.Count(b => b < 2);

        Assert.True(belowTwoSeconds >= 5, $"Expected at least 5 boundaries below 2s, found {belowTwoSeconds}");
    }
}