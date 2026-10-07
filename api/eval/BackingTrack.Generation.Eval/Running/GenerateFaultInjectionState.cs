namespace BackingTrack.Generation.Eval.Running;

/// <summary>
/// Scoped, per-case mutable state read by <see cref="FaultInjectingGenerateStep"/>. One instance
/// per <see cref="WorkflowGenerationRunner"/> run (fresh <c>IServiceScope</c> per case), so
/// setting it before a run can't leak into another case.
/// </summary>
public sealed class GenerateFaultInjectionState
{
    /// <summary>Remaining number of generate calls to throw from before falling through to the real generator.</summary>
    public int ExceptionsToForce { get; set; }
}