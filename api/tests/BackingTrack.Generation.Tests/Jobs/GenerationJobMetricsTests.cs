using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Steps.FallbackGeneration;

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
    [InlineData(FallbackReason.GenerateError, "generate_error")]
    [InlineData(FallbackReason.ValidationExhausted, "validation_exhausted")]
    [InlineData(FallbackReason.CriticExhausted, "critic_exhausted")]
    [InlineData(FallbackReason.RepairError, "repair_error")]
    [InlineData(FallbackReason.CriticError, "critic_error")]
    public void RecordOutcome_WithFallbackReason_TagsTheCounterWithIt(FallbackReason reason, string expectedTag)
    {
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture capture = new(MeasurementCapture.JobCompletedInstrument);

        metrics.RecordOutcome(JobStates.CompletedViaFallback, GenerationMode.Precise, fallbackReason: reason);

        CapturedMeasurement measurement = Assert.Single(capture.Measurements);
        Assert.Equal("completed_via_fallback", measurement.Tags["outcome"]);
        Assert.Equal(expectedTag, measurement.Tags["fallback_reason"]);
    }

    [Fact]
    public void RecordOutcome_WithoutFallbackReason_AddsNoFallbackReasonTag()
    {
        // Only fallback jobs carry the tag, so every other series keeps its existing shape.
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture capture = new(MeasurementCapture.JobCompletedInstrument);

        metrics.RecordOutcome(JobStates.Complete, GenerationMode.Precise);

        Assert.False(Assert.Single(capture.Measurements).Tags.ContainsKey("fallback_reason"));
    }

    // End-to-end job duration: submission to terminal state, queue wait included (see GenerationJob.StartTime).
    private const string JobDurationInstrument = "generation_job.duration";

    [Theory]
    [InlineData(JobStates.Complete, "complete")]
    [InlineData(JobStates.CompletedViaFallback, "completed_via_fallback")]
    [InlineData(JobStates.Failed, "failed")]
    [InlineData(JobStates.Cancelled, "cancelled")]
    public void RecordOutcome_WithDuration_RecordsSecondsTaggedWithOutcomeAndMode(JobStates state, string expectedOutcome)
    {
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture duration = new(JobDurationInstrument);

        metrics.RecordOutcome(state, GenerationMode.Vibe, TimeSpan.FromSeconds(90.5));

        CapturedMeasurement measurement = Assert.Single(duration.Measurements);
        Assert.Equal(90.5, measurement.Value);
        Assert.Equal(expectedOutcome, measurement.Tags["outcome"]);
        Assert.Equal("vibe", measurement.Tags["mode"]);
        Assert.Equal(["mode", "outcome"], measurement.Tags.Keys.Order());
    }

    [Fact]
    public void RecordOutcome_WithDuration_StillCountsTheJobOnce()
    {
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture completed = new(MeasurementCapture.JobCompletedInstrument);
        using MeasurementCapture duration = new(JobDurationInstrument);

        metrics.RecordOutcome(JobStates.Complete, GenerationMode.Precise, TimeSpan.FromSeconds(10));

        Assert.Single(completed.Measurements);
        Assert.Single(duration.Measurements);
    }

    [Fact]
    public void RecordOutcome_NoDuration_CountsTheJobButRecordsNoDuration()
    {
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture completed = new(MeasurementCapture.JobCompletedInstrument);
        using MeasurementCapture duration = new(JobDurationInstrument);

        metrics.RecordOutcome(JobStates.Complete, GenerationMode.Precise);

        Assert.Single(completed.Measurements);
        Assert.Empty(duration.Measurements);
    }

    [Fact]
    public void RecordOutcome_ZeroDuration_IsRecorded()
    {
        // Only a negative duration is invalid; a job that finished within the clock's resolution took zero seconds.
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture duration = new(JobDurationInstrument);

        metrics.RecordOutcome(JobStates.Failed, GenerationMode.Precise, TimeSpan.Zero);

        Assert.Equal(0, Assert.Single(duration.Measurements).Value);
    }

    [Fact]
    public void RecordOutcome_NegativeDuration_RecordsNoDurationButStillCountsTheJob()
    {
        // A wall-clock step backwards between submission and finish mustn't put a negative value in a histogram.
        using GenerationJobMetrics metrics = new();
        using MeasurementCapture completed = new(MeasurementCapture.JobCompletedInstrument);
        using MeasurementCapture duration = new(JobDurationInstrument);

        metrics.RecordOutcome(JobStates.Complete, GenerationMode.Precise, TimeSpan.FromSeconds(-1));

        Assert.Single(completed.Measurements);
        Assert.Empty(duration.Measurements);
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