using System.Diagnostics.Metrics;

using BackingTrack.Generation.Jobs;

namespace BackingTrack.Generation.Tests.Jobs;

internal sealed record CapturedMeasurement(long Value, IReadOnlyDictionary<string, object?> Tags);

/// <summary>
/// Captures every measurement of one <see cref="GenerationJobMetrics"/> instrument. Subscribes by meter
/// name, so tests using it belong to <see cref="JobMetricsCollection"/> to stay isolated from each other.
/// </summary>
internal sealed class MeasurementCapture : IDisposable
{
    /// <summary>The per-job terminal-outcome counter, shared by every test that asserts on it.</summary>
    public const string JobCompletedInstrument = "generation_job.completed";

    private readonly MeterListener _listener = new();
    private readonly List<CapturedMeasurement> _measurements = [];
    private readonly Lock _lock = new();

    public MeasurementCapture(string instrumentName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == GenerationJobMetrics.MeterName && instrument.Name == instrumentName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            Dictionary<string, object?> tagMap = tags.ToArray().ToDictionary(t => t.Key, t => t.Value);
            lock (_lock)
                _measurements.Add(new CapturedMeasurement(value, tagMap));
        });
        _listener.Start();
    }

    public IReadOnlyList<CapturedMeasurement> Measurements
    {
        get
        {
            lock (_lock)
                return _measurements.ToList();
        }
    }

    public void Dispose() => _listener.Dispose();
}