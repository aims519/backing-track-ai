namespace BackingTrack.Generation.Jobs;

public enum JobStates
{
    Pending,
    InProgress,
    Complete,
    CompletedViaFallback,
    Failed,
    Cancelled
}