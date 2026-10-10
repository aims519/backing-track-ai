using System.Diagnostics.Metrics;

using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackRepair;

namespace BackingTrack.Generation.Eval.Running;

/// <summary>Token and call totals for one pipeline step against one model and service tier.</summary>
public sealed record LlmUsage(string Step, string Model, string ServiceTier, long Calls, long InputTokens, long OutputTokens);

/// <summary>
/// Captures the token usage the pipeline steps already emit as metrics, so the harness doesn't need
/// its own plumbing through the steps. The tags on those metrics carry the model and service tier
/// that were actually sent, so the report attributes cost to what ran rather than to what the
/// config says should have.
/// </summary>
/// <remarks>
/// Cases run sequentially (see <see cref="EvalRunner"/>), so a case's usage is whatever was recorded
/// between <see cref="Reset"/> and <see cref="Snapshot"/>. Running cases in parallel would need
/// per-case attribution instead.
/// </remarks>
public sealed class LlmUsageRecorder : IDisposable
{
    private record struct Key(string Step, string Model, string ServiceTier);

    private sealed class Totals
    {
        public long Calls;
        public long InputTokens;
        public long OutputTokens;
    }

    private static readonly IReadOnlyDictionary<string, string> StepByMeter = new Dictionary<string, string>
    {
        [TrackGenerationMetrics.MeterName] = "generate",
        [TrackRepairMetrics.MeterName] = "repair",
        [CriticMetrics.MeterName] = "critic"
    };

    private readonly Lock _gate = new();
    private readonly Dictionary<Key, Totals> _totals = [];
    private readonly MeterListener _listener = new();

    public LlmUsageRecorder()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (StepByMeter.ContainsKey(instrument.Meter.Name) && IsUsageInstrument(instrument.Name))
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(OnMeasurement);
        _listener.Start();
    }

    public void Reset()
    {
        lock (_gate)
            _totals.Clear();
    }

    public IReadOnlyList<LlmUsage> Snapshot()
    {
        lock (_gate)
        {
            return _totals
                .Select(pair => new LlmUsage(
                    pair.Key.Step, pair.Key.Model, pair.Key.ServiceTier,
                    pair.Value.Calls, pair.Value.InputTokens, pair.Value.OutputTokens))
                .OrderBy(u => u.Step, StringComparer.Ordinal)
                .ThenBy(u => u.Model, StringComparer.Ordinal)
                .ToList();
        }
    }

    public void Dispose() => _listener.Dispose();

    // "<step>.call.input_tokens", "<step>.call.output_tokens" and the "<step>.calls" counter.
    private static bool IsUsageInstrument(string name) =>
        name.EndsWith(".call.input_tokens", StringComparison.Ordinal)
        || name.EndsWith(".call.output_tokens", StringComparison.Ordinal)
        || name.EndsWith(".calls", StringComparison.Ordinal);

    private void OnMeasurement(
        Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        string? model = null;
        string? serviceTier = null;
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (tag.Key == "model")
                model = tag.Value?.ToString();
            else if (tag.Key == "service_tier")
                serviceTier = tag.Value?.ToString();
        }

        Key key = new(StepByMeter[instrument.Meter.Name], model ?? "unknown", serviceTier ?? "unknown");

        lock (_gate)
        {
            if (!_totals.TryGetValue(key, out Totals? totals))
                _totals[key] = totals = new Totals();

            if (instrument.Name.EndsWith(".call.input_tokens", StringComparison.Ordinal))
                totals.InputTokens += value;
            else if (instrument.Name.EndsWith(".call.output_tokens", StringComparison.Ordinal))
                totals.OutputTokens += value;
            else
                totals.Calls += value;
        }
    }
}