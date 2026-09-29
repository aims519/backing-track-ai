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

    /// <summary>
    /// Inclusive lower bound on the critic attempt count the workflow reports — same "1 = no
    /// repair needed" indexing as <see cref="MinAttempts"/>, not a rejection count: 0 = the
    /// critic never ran (fallback path), 1 = it passed the first candidate outright, 2+ = it
    /// rejected at least once before passing. Set to 2 on a <c>CriticTriggering</c> case to
    /// assert the critic → regenerate loop actually ran, not just that the case passed.
    /// </summary>
    public int MinCriticAttempts { get; init; }

    /// <summary>Inclusive upper bound on the critic attempt count, when the case constrains it.</summary>
    public int? MaxCriticAttempts { get; init; }

    /// <summary>
    /// Inclusive lower bound on the average number of timekeeping hits (hi-hat or ride) per bar.
    /// Averaged rather than checked per bar so a legitimate fill bar doesn't fail the case —
    /// the failure this guards against is a whole groove with the timekeeping thinned out or
    /// missing, which left a rock track sounding empty.
    /// </summary>
    public double? MinAverageTimekeepingHitsPerBar { get; init; }
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