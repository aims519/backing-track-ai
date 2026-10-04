using BackingTrack.Generation.Jobs;

namespace BackingTrack.Generation.Tests.Jobs;

// MeterListener subscribes by meter name, so it would also see measurements from other
// GenerationJobMetrics instances (e.g. the worker tests). Sharing a collection serialises them.
[Collection(JobMetricsCollection.Name)]
public class GenerationJobMetricsTests
{
    [Theory]
    [InlineData(JobStates.Complete, "complete")]
    [InlineData(JobStates.CompletedViaFallback, "completed_via_fallback")]
    [InlineData(JobStates.Failed, "failed")]
    [InlineData(JobStates.Cancelled, "cancelled")]
    public void RecordOutcome_TerminalState_IncrementsCounterTaggedWithOutcome(JobStates state, string expectedOutcome)
    {
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture capture = new(MeasurementCapture.JobCompletedInstrument);

        metrics.RecordOutcome(state, GenerationMode.Precise);

        CapturedMeasurement measurement = Assert.Single(capture.Measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal(expectedOutcome, measurement.Tags["outcome"]);
    }

    [Theory]
    [InlineData(GenerationMode.Precise, "precise")]
    [InlineData(GenerationMode.Vibe, "vibe")]
    public void RecordOutcome_TagsWithGenerationMode(GenerationMode mode, string expectedMode)
    {
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture capture = new(MeasurementCapture.JobCompletedInstrument);

        metrics.RecordOutcome(JobStates.Complete, mode);

        Assert.Equal(expectedMode, Assert.Single(capture.Measurements).Tags["mode"]);
    }

    [Fact]
    public void RecordOutcome_CalledPerJob_EmitsOneMeasurementEach()
    {
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture capture = new(MeasurementCapture.JobCompletedInstrument);

        metrics.RecordOutcome(JobStates.Complete, GenerationMode.Precise);
        metrics.RecordOutcome(JobStates.Complete, GenerationMode.Precise);
        metrics.RecordOutcome(JobStates.CompletedViaFallback, GenerationMode.Vibe);

        Assert.Equal(3, capture.Measurements.Count);
        Assert.Equal(2, capture.Measurements.Count(m => (string?)m.Tags["outcome"] == "complete"));
        Assert.Equal(1, capture.Measurements.Count(m => (string?)m.Tags["outcome"] == "completed_via_fallback"));
    }

    [Fact]
    public void RecordOutcome_DoesNotTagWithHighCardinalityValues()
    {
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture capture = new(MeasurementCapture.JobCompletedInstrument);

        metrics.RecordOutcome(JobStates.Failed, GenerationMode.Vibe);

        // Unbounded tag values (job id, track id) would create a time series per job.
        Assert.Equal(["mode", "outcome"], Assert.Single(capture.Measurements).Tags.Keys.Order());
    }

    [Theory]
    [InlineData(JobStates.Pending)]
    [InlineData(JobStates.InProgress)]
    public void RecordOutcome_NonTerminalState_Throws(JobStates state)
    {
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture capture = new(MeasurementCapture.JobCompletedInstrument);

        Assert.Throws<ArgumentOutOfRangeException>(() => metrics.RecordOutcome(state, GenerationMode.Precise));
        Assert.Empty(capture.Measurements);
    }

    [Fact]
    public void RecordAttempts_TagsAttemptsUsedWithExactAttemptsAndOutcome()
    {
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture capture = new("generation_job.attempts_used");

        metrics.RecordAttempts(2, "success");

        CapturedMeasurement measurement = Assert.Single(capture.Measurements);
        // The tag is a boxed long (RecordAttempts takes a long); an int literal would never compare equal.
        Assert.Equal(2L, measurement.Tags["attempts"]);
        Assert.Equal("success", measurement.Tags["outcome"]);
    }
}

[CollectionDefinition(Name)]
public class JobMetricsCollection
{
    public const string Name = "GenerationJobMetrics";
}