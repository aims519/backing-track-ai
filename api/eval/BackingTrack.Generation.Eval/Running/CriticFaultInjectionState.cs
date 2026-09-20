namespace BackingTrack.Generation.Eval.Running;

/// <summary>
/// Scoped, per-case mutable state read by <see cref="FaultInjectingCriticStep"/>. One instance
/// per <see cref="WorkflowGenerationRunner"/> run (fresh <c>IServiceScope</c> per case), so
/// setting it before a run can't leak into another case.
/// </summary>
public sealed class CriticFaultInjectionState
{
    /// <summary>Remaining number of critic reviews to force-fail before falling through to the real critic.</summary>
    public int RejectionsToForce { get; set; }
}