namespace BackingTrack.Generation.Eval.Model;

/// <summary>
/// One golden-set entry: a fixed input plus the outcome a healthy pipeline should produce.
/// Cases live one-per-line as JSON in <c>GoldenSet/*.jsonl</c> and are meant to be reviewed in
/// a diff like any other source. Grow the set from real failures — every production bug worth
/// fixing is worth a case that would have caught it.
/// </summary>
public sealed record EvalCase
{
    /// <summary>Stable, unique, human-readable id (e.g. <c>baseline-blues-a</c>). Never reused.</summary>
    public required string Id { get; init; }

    public required EvalCategory Category { get; init; }

    /// <summary>One line on what this case probes.</summary>
    public required string Description { get; init; }

    /// <summary>Why this input earns a place in the golden set — the failure it was distilled from, or the risk it covers.</summary>
    public string? Rationale { get; init; }

    public required GenerationInput Input { get; init; }

    public required CaseExpectations Expect { get; init; }

    /// <summary>
    /// Harness-only test control, not part of <see cref="Input"/> — a real client never sends
    /// this. When &gt; 0, <c>FaultInjectingCriticStep</c> forces this many critic reviews to fail
    /// deterministically before letting the real critic judge, so a <c>CriticTriggering</c> case
    /// can guarantee the critic → regenerate loop runs instead of hoping a "risky" input draws a
    /// genuine rejection from the model.
    /// </summary>
    public int ForceCriticRejections { get; init; }

    /// <summary>
    /// Harness-only test control, not part of <see cref="Input"/> — a real client never sends
    /// this. When &gt; 0, <c>FaultInjectingValidateStep</c> forces this many validations to fail
    /// deterministically before letting the real validator judge, so a <c>Fallback</c> case can
    /// guarantee the validator's repair-budget exhaustion → fallback branch runs instead of
    /// hoping a "hard" input genuinely exhausts the repair loop.
    /// </summary>
    public int ForceValidationFailures { get; init; }
}