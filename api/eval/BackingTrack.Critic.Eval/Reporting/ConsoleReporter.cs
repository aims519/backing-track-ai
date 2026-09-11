using BackingTrack.Critic.Eval.Running;

namespace BackingTrack.Critic.Eval.Reporting;

public static class ConsoleReporter
{
    public static bool Print(IReadOnlyList<FixtureOutcome> outcomes)
    {
        bool allCorrectAndConsistent = true;

        foreach (FixtureOutcome outcome in outcomes)
        {
            allCorrectAndConsistent &= outcome.IsCorrect;

            string verdictSummary = string.Join(", ", outcome.Runs.Select(r => $"{(r.Pass ? "pass" : "fail")}={r.Score:F1}"));
            string status = !outcome.IsConsistent ? "INCONSISTENT" : outcome.IsCorrect ? "ok" : "WRONG VERDICT";

            Console.WriteLine($"[{status}] {outcome.Fixture.Id} (expected {(outcome.Fixture.ExpectedPass ? "pass" : "fail")})");
            Console.WriteLine($"    {outcome.Fixture.Description}");
            Console.WriteLine($"    runs: {verdictSummary}");
            Console.WriteLine($"    mean score {outcome.MeanScore:F1}, stddev {outcome.ScoreStdDev:F2}");

            if (outcome.Fixture.ExpectedFailingDimensions.Count > 0)
            {
                foreach (FixtureRun run in outcome.Runs)
                {
                    IReadOnlyList<string> citedDimensions = run.Citations.Select(c => c.Dimension.ToString()).Distinct().ToList();
                    bool citesExpectedDimension = outcome.Fixture.ExpectedFailingDimensions.All(d => citedDimensions.Contains(d.ToString()));
                    if (!citesExpectedDimension)
                    {
                        Console.WriteLine($"    NOTE: expected a citation on {string.Join(", ", outcome.Fixture.ExpectedFailingDimensions)}, got [{string.Join(", ", citedDimensions)}]");
                    }
                }
            }

            Console.WriteLine();
        }

        int correct = outcomes.Count(o => o.IsCorrect);
        int inconsistent = outcomes.Count(o => !o.IsConsistent);
        Console.WriteLine($"{correct}/{outcomes.Count} fixtures correct, {inconsistent} inconsistent across repeats.");

        return allCorrectAndConsistent;
    }
}