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
}