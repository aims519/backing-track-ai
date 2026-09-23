namespace BackingTrack.Generation.Jobs;

/// Reports a workflow stage's progress against a running job. A narrow seam over `JobStore` so
/// executors depend on "report my status" rather than the job store itself.
public interface IJobStatusEmitter
{
    void Emit(Guid jobId, string stage, int attempt, string message);
}