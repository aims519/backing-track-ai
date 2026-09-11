using System.Diagnostics;

using BackingTrack.Critic.Eval.Fixtures;
using BackingTrack.Generation.Steps.Critic;

using Microsoft.Extensions.Logging;

namespace BackingTrack.Critic.Eval.Running;

public sealed record FixtureRun(bool Pass, double Score, IReadOnlyList<CriticCitation> Citations);

public sealed record FixtureOutcome(CriticFixture Fixture, IReadOnlyList<FixtureRun> Runs)
{
    /// <summary>Every repeat run agreed with every other repeat run on pass/fail.</summary>
    public bool IsConsistent => Runs.Select(r => r.Pass).Distinct().Count() <= 1;

    /// <summary>The (consistent) verdict matches the fixture's known-correct verdict.</summary>
    public bool IsCorrect => IsConsistent && Runs[0].Pass == Fixture.ExpectedPass;

    public double MeanScore => Runs.Average(r => r.Score);

    public double ScoreStdDev
    {
        get
        {
            if (Runs.Count < 2)
                return 0;

            double mean = MeanScore;
            double variance = Runs.Sum(r => Math.Pow(r.Score - mean, 2)) / (Runs.Count - 1);
            return Math.Sqrt(variance);
        }
    }
}

/// <summary>
/// Drives the real <see cref="ICriticStep"/> (live LLM, not the stub used in
/// BackingTrack.Generation.Tests) against each fixture <paramref name="repeatCount"/> times, to
/// check both correctness (does the verdict match the fixture's known answer) and consistency
/// (does the judge agree with itself across repeats).
/// </summary>
public sealed class CriticEvalRunner(ICriticStep criticStep, ILogger<CriticEvalRunner> logger)
{
    public async Task<IReadOnlyList<FixtureOutcome>> RunAsync(IReadOnlyList<CriticFixture> fixtures, int repeatCount, CancellationToken cancellationToken)
    {
        List<FixtureOutcome> outcomes = [];
        for (int fixtureIndex = 0; fixtureIndex < fixtures.Count; fixtureIndex++)
        {
            CriticFixture fixture = fixtures[fixtureIndex];
            logger.LogInformation("[{Current}/{Total}] {FixtureId}: {Description}",
                fixtureIndex + 1, fixtures.Count, fixture.Id, fixture.Description);

            List<FixtureRun> runs = [];
            for (int i = 0; i < repeatCount; i++)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                CriticFinalReview review = await criticStep.ReviewCandidateTrackAsync(
                    fixture.CandidateTrack,
                    fixture.GenerationJob,
                    fixture.MatchedStyle,
                    fixture.MatchedChords,
                    fixture.MatchedScales,
                    cancellationToken);
                stopwatch.Stop();

                logger.LogInformation("    run {Run}/{RepeatCount}: {Verdict} score={Score:F1} ({ElapsedMs}ms, {CitationCount} citation(s))",
                    i + 1, repeatCount, review.Pass ? "pass" : "fail", review.Score, stopwatch.ElapsedMilliseconds, review.Citations.Count);

                foreach (CriticCitation citation in review.Citations)
                {
                    logger.LogInformation("        - {Dimension}: {Violation} [kb:{CitedKnowledgeBaseItemId}]",
                        citation.Dimension, citation.Violation, citation.CitedKnowledgeBaseItemId);
                }

                runs.Add(new FixtureRun(review.Pass, review.Score, review.Citations));
            }

            outcomes.Add(new FixtureOutcome(fixture, runs));
        }

        return outcomes;
    }
}
