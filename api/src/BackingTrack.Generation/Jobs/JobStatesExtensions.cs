namespace BackingTrack.Generation.Jobs;

public static class JobStatesExtensions
{
    public static string ToMetricTag(this JobStates state)
    {
        return state switch
        {
            JobStates.Pending => "pending",
            JobStates.InProgress => "in_progress",
            JobStates.Complete => "complete",
            JobStates.CompletedViaFallback => "completed_via_fallback",
            JobStates.Cancelled => "cancelled",
            JobStates.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }
}