using BackingTrack.Generation.Eval.Model;

using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Eval.Running;

/// <summary>Runs a set of golden cases and collects a verdict for each.</summary>
public sealed class EvalRunner(IGenerationRunner generationRunner, ILogger<EvalRunner> logger)
{
    public async Task<IReadOnlyList<CaseOutcome>> RunAsync(
        IReadOnlyList<EvalCase> cases, CancellationToken cancellationToken)
    {
        List<CaseOutcome> outcomes = new(cases.Count);

        // Deliberately sequential: generation is LLM-bound and rate-limited, and a readable
        // live log matters more than wall-clock here. Revisit with a bounded parallelism if
        // the golden set grows past a few dozen cases.
        foreach (EvalCase evalCase in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogInformation("▶ {Id} [{Category}] — {Description}", evalCase.Id, evalCase.Category, evalCase.Description);

            CaseOutcome outcome;
            try
            {
                GenerationRunResult result = await generationRunner.RunAsync(evalCase, cancellationToken);
                outcome = CaseEvaluator.Evaluate(evalCase, result);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Case {Id} threw", evalCase.Id);
                outcome = new CaseOutcome
                {
                    Case = evalCase,
                    Verdict = CaseVerdict.Fail,
                    Notes = [$"harness error: {ex.Message}"]
                };
            }

            string track = outcome.Result?.TrackId is { } trackId ? $" track {trackId}" : "";
            string notes = outcome.Notes.Count > 0 ? " — " + string.Join("; ", outcome.Notes) : "";
            logger.LogInformation("  {Verdict}{Track}{Notes}", outcome.Verdict, track, notes);
            outcomes.Add(outcome);
        }

        return outcomes;
    }
}