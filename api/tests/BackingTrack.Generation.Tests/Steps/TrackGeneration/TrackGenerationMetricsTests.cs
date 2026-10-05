using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Tests.Jobs;

namespace BackingTrack.Generation.Tests.Steps.TrackGeneration;

// MeterListener subscribes by meter name, so test classes that create a TrackGenerationMetrics share this collection.
[CollectionDefinition(Name)]
public class TrackGenerationMetricsCollection
{
    public const string Name = "TrackGenerationMetrics";
}

[Collection(TrackGenerationMetricsCollection.Name)]
public class TrackGenerationMetricsTests
{
    private static MeasurementCapture Capture(string instrument) => new(instrument, TrackGenerationMetrics.MeterName);

    [Fact]
    public void RecordSuccess_TagsEveryInstrumentWithTheServiceTier()
    {
        using TrackGenerationMetrics metrics = new();
        using MeasurementCapture duration = Capture("track_generation.call.duration");
        using MeasurementCapture input = Capture("track_generation.call.input_tokens");
        using MeasurementCapture output = Capture("track_generation.call.output_tokens");
        using MeasurementCapture calls = Capture("track_generation.calls");

        metrics.RecordSuccess("OpenAi", "gpt-test", "flex", TimeSpan.FromSeconds(1), 10, 5);

        Assert.Equal("flex", Assert.Single(duration.Measurements).Tags["service_tier"]);
        Assert.Equal("flex", Assert.Single(input.Measurements).Tags["service_tier"]);
        Assert.Equal("flex", Assert.Single(output.Measurements).Tags["service_tier"]);
        Assert.Equal("flex", Assert.Single(calls.Measurements).Tags["service_tier"]);
    }

    [Fact]
    public void RecordFailure_TagsDurationAndCallCounterWithTheServiceTier()
    {
        using TrackGenerationMetrics metrics = new();
        using MeasurementCapture duration = Capture("track_generation.call.duration");
        using MeasurementCapture calls = Capture("track_generation.calls");

        metrics.RecordFailure("OpenAi", "gpt-test", "default", TimeSpan.FromSeconds(1));

        Assert.Equal("default", Assert.Single(duration.Measurements).Tags["service_tier"]);
        CapturedMeasurement call = Assert.Single(calls.Measurements);
        Assert.Equal("default", call.Tags["service_tier"]);
        Assert.Equal("failure", call.Tags["outcome"]);
    }
}