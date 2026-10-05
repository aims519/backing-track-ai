using BackingTrack.Generation.Steps.TrackRepair;
using BackingTrack.Generation.Tests.Jobs;

namespace BackingTrack.Generation.Tests.Steps.TrackRepair;

// MeterListener subscribes by meter name, so test classes that create a TrackRepairMetrics share this collection.
[CollectionDefinition(Name)]
public class TrackRepairMetricsCollection
{
    public const string Name = "TrackRepairMetrics";
}

[Collection(TrackRepairMetricsCollection.Name)]
public class TrackRepairMetricsTests
{
    private static MeasurementCapture Capture(string instrument) => new(instrument, TrackRepairMetrics.MeterName);

    [Fact]
    public void RecordSuccess_TagsEveryInstrumentWithTheServiceTier()
    {
        using TrackRepairMetrics metrics = new();
        using MeasurementCapture duration = Capture("track_repair.call.duration");
        using MeasurementCapture input = Capture("track_repair.call.input_tokens");
        using MeasurementCapture output = Capture("track_repair.call.output_tokens");
        using MeasurementCapture calls = Capture("track_repair.calls");

        metrics.RecordSuccess("OpenAi", "gpt-test", "flex", TimeSpan.FromSeconds(1), 10, 5);

        Assert.Equal("flex", Assert.Single(duration.Measurements).Tags["service_tier"]);
        Assert.Equal("flex", Assert.Single(input.Measurements).Tags["service_tier"]);
        Assert.Equal("flex", Assert.Single(output.Measurements).Tags["service_tier"]);
        Assert.Equal("flex", Assert.Single(calls.Measurements).Tags["service_tier"]);
    }

    [Fact]
    public void RecordFailure_TagsDurationAndCallCounterWithTheServiceTier()
    {
        using TrackRepairMetrics metrics = new();
        using MeasurementCapture duration = Capture("track_repair.call.duration");
        using MeasurementCapture calls = Capture("track_repair.calls");

        metrics.RecordFailure("OpenAi", "gpt-test", "default", TimeSpan.FromSeconds(1));

        Assert.Equal("default", Assert.Single(duration.Measurements).Tags["service_tier"]);
        CapturedMeasurement call = Assert.Single(calls.Measurements);
        Assert.Equal("default", call.Tags["service_tier"]);
        Assert.Equal("failure", call.Tags["outcome"]);
    }
}