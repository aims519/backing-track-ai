using System.Diagnostics;

namespace BackingTrack.Generation.Tests.Steps.Critic;

/// <summary>Collects every activity a given <see cref="ActivitySource"/> stops, so a test can inspect its tags and status.</summary>
internal sealed class ActivityCapture : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];
    private readonly Lock _lock = new();

    public ActivityCapture(string sourceName)
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_lock)
                    _activities.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Activities
    {
        get
        {
            lock (_lock)
                return _activities.ToList();
        }
    }

    public void Dispose() => _listener.Dispose();
}