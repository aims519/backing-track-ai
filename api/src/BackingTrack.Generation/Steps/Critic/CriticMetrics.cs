using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace BackingTrack.Generation.Steps.Critic;

public class CriticMetrics : IDisposable
{
    public const string MeterName = "BackingTrack.Critic";

    private readonly Meter _meter;
    private readonly Histogram<double> _callDuration;
    private readonly Histogram<long> _inputTokens;
    private readonly Histogram<long> _outputTokens;
    private readonly Counter<long> _calls;

    public CriticMetrics()
    {
        _meter = new Meter(MeterName);
        _callDuration = _meter.CreateHistogram<double>("critic.call.duration",
            unit: "s", description: "Duration of a critic LLM call");
        _inputTokens = _meter.CreateHistogram<long>("critic.call.input_tokens",
            description: "Input tokens consumed by a critic LLM call");
        _outputTokens = _meter.CreateHistogram<long>("critic.call.output_tokens",
            description: "Output tokens consumed by a critic LLM call");
        _calls = _meter.CreateCounter<long>("critic.calls",
            description: "Number of critic LLM calls, tagged by outcome");
    }

    public void RecordSuccess(string provider, string model, TimeSpan duration, long? inputTokens, long? outputTokens)
    {
        TagList tags = new() { { "provider", provider }, { "model", model } };

        RecordTokens(tags, inputTokens, outputTokens);
        _callDuration.Record(duration.TotalSeconds, tags);
        _calls.Add(1, new TagList { { "provider", provider }, { "model", model }, { "outcome", "success" } });
    }

    public void RecordFailure(string provider, string model, TimeSpan duration, long? inputTokens = null, long? outputTokens = null)
    {
        TagList tags = new() { { "provider", provider }, { "model", model } };
        
        RecordTokens(tags, inputTokens, outputTokens);
        _callDuration.Record(duration.TotalSeconds, new TagList { { "provider", provider }, { "model", model } });
        _calls.Add(1, new TagList { { "provider", provider }, { "model", model }, { "outcome", "failure" } });
    }

    private void RecordTokens(TagList tags, long? inputTokens, long? outputTokens)
    {
        if (inputTokens is { } input)
            _inputTokens.Record(input, tags);
        if (outputTokens is { } output)
            _outputTokens.Record(output, tags);
    }

    public void Dispose() => _meter.Dispose();
}