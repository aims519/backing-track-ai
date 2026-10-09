namespace BackingTrack.Generation.Jobs;

/// Reports a workflow stage's progress against a running job. A narrow seam over `JobStore` so
/// executors depend on "report my status" rather than the job store itself.
public interface IJobStatusEmitter
{
    /// `attempt` is the generation round (the critic attempt), the same for every stage within a round,
    /// so the app can badge a retried round. It is not the validate/repair loop count within a round.
    void Emit(Guid jobId, string stage, int attempt, string message);
}