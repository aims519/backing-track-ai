using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BackingTrack.Generation.Steps.TrackRepair;

public class TrackRepairMetrics : IDisposable
{
    public const string MeterName = "BackingTrack.TrackRepair";

    private readonly Meter _meter;
    private readonly Histogram<double> _callDuration;
    private readonly Histogram<long> _inputTokens;
    private readonly Histogram<long> _outputTokens;
    private readonly Counter<long> _calls;

    public TrackRepairMetrics()
    {
        _meter = new Meter(MeterName);
        _callDuration = _meter.CreateHistogram("track_repair.call.duration",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = Constants.LlmCallDurationBuckets },
            unit: "s", description: "Duration of a track repair LLM call");
        _inputTokens = _meter.CreateHistogram<long>("track_repair.call.input_tokens",
            description: "Input tokens consumed by a track repair LLM call");
        _outputTokens = _meter.CreateHistogram<long>("track_repair.call.output_tokens",
            description: "Output tokens consumed by a track repair LLM call");
        _calls = _meter.CreateCounter<long>("track_repair.calls",
            description: "Number of track repair LLM calls, tagged by outcome");
    }

    public void RecordSuccess(string provider, string model, string serviceTier, TimeSpan duration, long? inputTokens, long? outputTokens)
    {
        TagList tags = new() { { "provider", provider }, { "model", model }, { "service_tier", serviceTier } };
        _callDuration.Record(duration.TotalSeconds, tags);

        if (inputTokens is { } input)
            _inputTokens.Record(input, tags);
        if (outputTokens is { } output)
            _outputTokens.Record(output, tags);

        TagList callTags = tags;
        callTags.Add("outcome", "success");
        _calls.Add(1, callTags);
    }

    public void RecordFailure(string provider, string model, string serviceTier, TimeSpan duration)
    {
        TagList tags = new() { { "provider", provider }, { "model", model }, { "service_tier", serviceTier } };

        _callDuration.Record(duration.TotalSeconds, tags);

        TagList callTags = tags;
        callTags.Add("outcome", "failure");
        _calls.Add(1, callTags);
    }

    public void Dispose() => _meter.Dispose();
}