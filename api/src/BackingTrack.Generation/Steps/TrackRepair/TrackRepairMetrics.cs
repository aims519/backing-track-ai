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
        _callDuration = _meter.CreateHistogram<double>("track_repair.call.duration",
            unit: "s", description: "Duration of a track repair LLM call");
        _inputTokens = _meter.CreateHistogram<long>("track_repair.call.input_tokens",
            description: "Input tokens consumed by a track repair LLM call");
        _outputTokens = _meter.CreateHistogram<long>("track_repair.call.output_tokens",
            description: "Output tokens consumed by a track repair LLM call");
        _calls = _meter.CreateCounter<long>("track_repair.calls",
            description: "Number of track repair LLM calls, tagged by outcome");
    }

    public void RecordSuccess(string provider, string model, TimeSpan duration, long? inputTokens, long? outputTokens)
    {
        TagList tags = new() { { "provider", provider }, { "model", model } };
        _callDuration.Record(duration.TotalSeconds, tags);

        if (inputTokens is { } input)
            _inputTokens.Record(input, tags);
        if (outputTokens is { } output)
            _outputTokens.Record(output, tags);

        _calls.Add(1, new TagList { { "provider", provider }, { "model", model }, { "outcome", "success" } });
    }

    public void RecordFailure(string provider, string model, TimeSpan duration)
    {
        _callDuration.Record(duration.TotalSeconds, new TagList { { "provider", provider }, { "model", model } });
        _calls.Add(1, new TagList { { "provider", provider }, { "model", model }, { "outcome", "failure" } });
    }

    public void Dispose() => _meter.Dispose();
}