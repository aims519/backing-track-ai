using BackingTrack.Generation.Eval.Model;

namespace BackingTrack.Generation.Eval.Running;

public enum CaseVerdict
{
    Pass,
    Fail,

    /// <summary>
    /// The pipeline stage this case targets isn't built yet (the fallback generator). Not a
    /// failure, but kept visible so the pending surface doesn't get forgotten.
    /// </summary>
    Pending
}

public sealed record CaseOutcome
{
    public required EvalCase Case { get; init; }
    public required CaseVerdict Verdict { get; init; }
    public GenerationRunResult? Result { get; init; }

    /// <summary>Reasons the case failed, or — for a pending verdict — why it was skipped.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Compares a run result against a case's expectations. Binary, deterministic checks only.
/// </summary>
public static class CaseEvaluator
{
    public static CaseOutcome Evaluate(EvalCase evalCase, GenerationRunResult result)
    {
        List<string> notes = [];

        ActualOutcome expected = evalCase.Expect.Outcome switch
        {
            ExpectedOutcome.Succeeds => ActualOutcome.Succeeded,
            ExpectedOutcome.SucceedsViaFallback => ActualOutcome.SucceededViaFallback,
            _ => ActualOutcome.Failed
        };

        if (result.Outcome != expected)
        {
            string detail = result.FailureReason is { Length: > 0 } reason ? $" — {reason}" : "";
            notes.Add($"expected outcome {expected}, got {result.Outcome}{detail}");
        }

        if (result.Attempts is { } attempts)
        {
            if (attempts < evalCase.Expect.MinAttempts)
                notes.Add($"expected ≥ {evalCase.Expect.MinAttempts} attempt(s), got {attempts}");
            if (evalCase.Expect.MaxAttempts is { } max && attempts > max)
                notes.Add($"expected ≤ {max} attempt(s), got {attempts}");
        }

        if (result.CriticAttempts is { } criticAttempts)
        {
            if (criticAttempts < evalCase.Expect.MinCriticAttempts)
                notes.Add($"expected ≥ {evalCase.Expect.MinCriticAttempts} critic attempt(s), got {criticAttempts}");
            if (evalCase.Expect.MaxCriticAttempts is { } maxCritic && criticAttempts > maxCritic)
                notes.Add($"expected ≤ {maxCritic} critic attempt(s), got {criticAttempts}");
        }

        return new CaseOutcome
        {
            Case = evalCase,
            Verdict = notes.Count == 0 ? CaseVerdict.Pass : CaseVerdict.Fail,
            Result = result,
            Notes = notes
        };
    }
}