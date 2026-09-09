namespace BackingTrack.Generation.Eval.Model;

/// <summary>
/// What a passing run looks like for a case. Deterministic, checkable properties only —
/// subjective musical judgement is out of scope for the harness.
/// </summary>
public sealed record CaseExpectations
{
    /// <summary>The terminal outcome the pipeline should reach.</summary>
    public required ExpectedOutcome Outcome { get; init; }

    /// <summary>
    /// Inclusive lower bound on the attempt count the workflow reports. A baseline case
    /// expects 1; a validator case expects at least 2 (first candidate + at least one repair).
    /// </summary>
    public int MinAttempts { get; init; } = 1;

    /// <summary>Inclusive upper bound on the attempt count, when the case constrains it.</summary>
    public int? MaxAttempts { get; init; }
}

public enum ExpectedOutcome
{
    /// <summary>The agentic pipeline returns a validated track.</summary>
    Succeeds,

    /// <summary>The agentic pipeline gives up and the rule-based fallback produces the track.</summary>
    SucceedsViaFallback,

    /// <summary>Generation fails outright — used for inputs that should be rejected, not salvaged.</summary>
    Fails
}