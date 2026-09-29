using BackingTrack.Generation.Eval.Model;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;

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
    // General MIDI: closed, pedal and open hi-hat; ride 1, ride bell, ride 2.
    private static readonly IReadOnlySet<int> TimekeepingMidiNotes = new HashSet<int> { 42, 44, 46, 51, 53, 59 };

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

        if (evalCase.Expect.MinAverageTimekeepingHitsPerBar is { } minTimekeeping && result.Track is { } track)
        {
            double average = AverageTimekeepingHitsPerBar(track, evalCase.Input.TimeSignatureBeatCount);
            if (average < minTimekeeping)
                notes.Add($"expected ≥ {minTimekeeping} timekeeping hit(s) per bar on average, got {average:0.##}");
        }

        return new CaseOutcome
        {
            Case = evalCase,
            Verdict = notes.Count == 0 ? CaseVerdict.Pass : CaseVerdict.Fail,
            Result = result,
            Notes = notes
        };
    }

    public static double AverageTimekeepingHitsPerBar(CandidateTrack track, int beatsPerBar)
    {
        IReadOnlyList<Bar> bars = new CriticCandidateTrack(track, beatsPerBar).Bars;
        if (bars.Count == 0)
            return 0;

        return bars.Average(bar => bar.Drums.Count(n => TimekeepingMidiNotes.Contains(n.MidiNote)));
    }
}