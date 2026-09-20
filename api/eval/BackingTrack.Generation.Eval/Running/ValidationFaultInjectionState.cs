namespace BackingTrack.Generation.Eval.Running;

/// <summary>
/// Scoped, per-case mutable state read by <see cref="FaultInjectingValidateStep"/>. One instance
/// per <see cref="WorkflowGenerationRunner"/> run (fresh <c>IServiceScope</c> per case), so
/// setting it before a run can't leak into another case.
/// </summary>
public sealed class ValidationFaultInjectionState
{
    /// <summary>Remaining number of validations to force-fail before falling through to the real validator.</summary>
    public int FailuresToForce { get; set; }
}