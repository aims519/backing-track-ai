using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Tests.Jobs;

namespace BackingTrack.Generation.Tests.Steps.Critic;

[Collection(CriticMetricsCollection.Name)]
public class CriticMetricsTests
{
    private static MeasurementCapture Capture(string instrument) => new(instrument, CriticMetrics.MeterName);

    [Fact]
    public void RecordSuccess_RecordsDurationInSecondsTaggedWithProviderAndModel()
    {
        using CriticMetrics metrics = new();
        using MeasurementCapture capture = Capture("critic.call.duration");

        metrics.RecordSuccess("OpenAi", "gpt-test", TimeSpan.FromMilliseconds(2500), 10, 5);

        CapturedMeasurement measurement = Assert.Single(capture.Measurements);
        Assert.Equal(2.5, measurement.Value);
        Assert.Equal("OpenAi", measurement.Tags["provider"]);
        Assert.Equal("gpt-test", measurement.Tags["model"]);
    }

    [Fact]
    public void RecordSuccess_RecordsInputAndOutputTokens()
    {
        using CriticMetrics metrics = new();
        using MeasurementCapture input = Capture("critic.call.input_tokens");
        using MeasurementCapture output = Capture("critic.call.output_tokens");

        metrics.RecordSuccess("OpenAi", "gpt-test", TimeSpan.FromSeconds(1), 120, 45);

        CapturedMeasurement inputMeasurement = Assert.Single(input.Measurements);
        CapturedMeasurement outputMeasurement = Assert.Single(output.Measurements);
        Assert.Equal(120, inputMeasurement.Value);
        Assert.Equal(45, outputMeasurement.Value);
        Assert.Equal("gpt-test", inputMeasurement.Tags["model"]);
        Assert.Equal("gpt-test", outputMeasurement.Tags["model"]);
    }

    [Fact]
    public void RecordSuccess_NoUsageReported_SkipsTokenMeasurementsButStillRecordsDurationAndCall()
    {
        using CriticMetrics metrics = new();
        using MeasurementCapture input = Capture("critic.call.input_tokens");
        using MeasurementCapture output = Capture("critic.call.output_tokens");
        using MeasurementCapture duration = Capture("critic.call.duration");
        using MeasurementCapture calls = Capture("critic.calls");

        metrics.RecordSuccess("OpenAi", "gpt-test", TimeSpan.FromSeconds(1), inputTokens: null, outputTokens: null);

        Assert.Empty(input.Measurements);
        Assert.Empty(output.Measurements);
        Assert.Single(duration.Measurements);
        Assert.Single(calls.Measurements);
    }

    [Fact]
    public void RecordSuccess_IncrementsCallCounterWithSuccessOutcome()
    {
        using CriticMetrics metrics = new();
        using MeasurementCapture calls = Capture("critic.calls");

        metrics.RecordSuccess("OpenAi", "gpt-test", TimeSpan.FromSeconds(1), 10, 5);

        CapturedMeasurement measurement = Assert.Single(calls.Measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal("success", measurement.Tags["outcome"]);
        Assert.Equal("OpenAi", measurement.Tags["provider"]);
        Assert.Equal("gpt-test", measurement.Tags["model"]);
    }

    [Fact]
    public void RecordFailure_IncrementsCallCounterWithFailureOutcomeAndRecordsDuration()
    {
        using CriticMetrics metrics = new();
        using MeasurementCapture calls = Capture("critic.calls");
        using MeasurementCapture duration = Capture("critic.call.duration");

        metrics.RecordFailure("OpenAi", "gpt-test", TimeSpan.FromSeconds(3));

        CapturedMeasurement call = Assert.Single(calls.Measurements);
        Assert.Equal("failure", call.Tags["outcome"]);
        Assert.Equal(3.0, Assert.Single(duration.Measurements).Value);
    }

    [Fact]
    public void RecordFailure_ResponseArrivedWithUsage_RecordsTokens()
    {
        // A response that came back but couldn't be used still cost tokens.
        using CriticMetrics metrics = new();
        using MeasurementCapture input = Capture("critic.call.input_tokens");
        using MeasurementCapture output = Capture("critic.call.output_tokens");

        metrics.RecordFailure("OpenAi", "gpt-test", TimeSpan.FromSeconds(1), 120, 45);

        Assert.Equal(120, Assert.Single(input.Measurements).Value);
        Assert.Equal(45, Assert.Single(output.Measurements).Value);
    }

    [Fact]
    public void RecordFailure_NoResponse_RecordsNoTokens()
    {
        using CriticMetrics metrics = new();
        using MeasurementCapture input = Capture("critic.call.input_tokens");
        using MeasurementCapture output = Capture("critic.call.output_tokens");

        metrics.RecordFailure("OpenAi", "gpt-test", TimeSpan.FromSeconds(1));

        Assert.Empty(input.Measurements);
        Assert.Empty(output.Measurements);
    }
}