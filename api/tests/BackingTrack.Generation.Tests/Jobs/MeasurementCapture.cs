using System.Diagnostics.Metrics;

using BackingTrack.Generation.Jobs;

namespace BackingTrack.Generation.Tests.Jobs;

internal sealed record CapturedMeasurement(double Value, IReadOnlyDictionary<string, object?> Tags);

/// <summary>
/// Captures every measurement of one instrument, whether it records ints, longs or doubles. Subscribes by meter
/// name (the job meter unless told otherwise), so tests that create metrics instances of the same meter
/// must share a collection - e.g. <see cref="JobMetricsCollection"/> - to stay isolated from each other.
/// </summary>
internal sealed class MeasurementCapture : IDisposable
{
    /// <summary>The per-job terminal-outcome counter, shared by every test that asserts on it.</summary>
    public const string JobCompletedInstrument = "generation_job.completed";

    private readonly MeterListener _listener = new();
    private readonly List<CapturedMeasurement> _measurements = [];
    private readonly Lock _lock = new();

    public MeasurementCapture(string instrumentName, string meterName = GenerationJobMetrics.MeterName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName && instrument.Name == instrumentName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<int>((_, value, tags, _) => Add(value, tags));
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) => Add(value, tags));
        _listener.SetMeasurementEventCallback<double>((_, value, tags, _) => Add(value, tags));
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

    /// <summary>Asks observable instruments (gauges) to report now; they only record when polled.</summary>
    public void Poll() => _listener.RecordObservableInstruments();

    public void Dispose() => _listener.Dispose();

    private void Add(double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        Dictionary<string, object?> tagMap = tags.ToArray().ToDictionary(t => t.Key, t => t.Value);
        lock (_lock)
            _measurements.Add(new CapturedMeasurement(value, tagMap));
    }
}