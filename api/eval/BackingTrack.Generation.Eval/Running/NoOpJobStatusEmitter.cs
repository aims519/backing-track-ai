using BackingTrack.Generation.Jobs;

namespace BackingTrack.Generation.Eval.Running;

/// <summary>
/// The harness runs the workflow without a <see cref="JobStore"/> (no <c>AddJobProcessing()</c>),
/// so there is no job to report progress against, and nothing reads it — the verdict comes from
/// the workflow's terminal output instead.
/// </summary>
public sealed class NoOpJobStatusEmitter : IJobStatusEmitter
{
    public void Emit(Guid jobId, string stage, int attempt, string message)
    {
    }
}