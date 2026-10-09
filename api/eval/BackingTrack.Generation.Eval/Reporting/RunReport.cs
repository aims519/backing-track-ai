using BackingTrack.Generation.Eval.Model;
using BackingTrack.Generation.Eval.Running;
using BackingTrack.Generation.Steps.FallbackGeneration;

namespace BackingTrack.Generation.Eval.Reporting;

/// <summary>An aggregated view of one harness run — the thing the reporters render.</summary>
public sealed record RunReport
{
    public required DateTimeOffset RunAtUtc { get; init; }

    /// <summary>Absolute path where a case's persisted track lands, as <c>&lt;trackId&gt;.json</c>.</summary>
    public string? TrackStorageDirectory { get; init; }

    public required IReadOnlyList<CategoryTally> ByCategory { get; init; }
    public required IReadOnlyList<CaseLine> Cases { get; init; }

    public int Passed => Cases.Count(c => c.Verdict == CaseVerdict.Pass);
    public int Failed => Cases.Count(c => c.Verdict == CaseVerdict.Fail);
    public int Pending => Cases.Count(c => c.Verdict == CaseVerdict.Pending);

    /// <summary>A run passes when nothing failed. Pending cases don't gate — they're not built yet.</summary>
    public bool IsPass => Failed == 0;

    /// <summary>
    /// Cases that ran the real pipeline end to end, i.e. without a harness fault-injection control
    /// (<c>ForceCriticRejections</c>, <c>ForceValidationFailures</c>, <c>ForceGenerateExceptions</c>).
    /// Fault-injected cases fall back by design, so they're left out of the fallback rate.
    /// </summary>
    public int OrganicCases => Cases.Count(c => !c.FaultInjected);

    public int OrganicFallbacks => Cases.Count(c => !c.FaultInjected && c.Outcome == ActualOutcome.SucceededViaFallback);

    /// <summary>Share of organic cases that fell back to the rule-based generator. Null when there were none.</summary>
    public double? OrganicFallbackRate => OrganicCases == 0 ? null : (double)OrganicFallbacks / OrganicCases;

    /// <summary>Token usage and cost across the whole run, by step, model and service tier.</summary>
    public required IReadOnlyList<UsageLine> Usage { get; init; }

    /// <summary>
    /// Estimated spend for the run. Null when any model that was called has no price in the
    /// <c>Pricing</c> config, so an unpriced model can't pass for a cheap one.
    /// </summary>
    public decimal? TotalCostUsd => Usage.Any(u => u.CostUsd is null) ? null : Usage.Sum(u => u.CostUsd!.Value);

    public static RunReport From(
        IReadOnlyList<CaseOutcome> outcomes, string? trackStorageDirectory = null, CostEstimator? costEstimator = null)
    {
        costEstimator ??= CostEstimator.None;

        List<CategoryTally> byCategory = Enum.GetValues<EvalCategory>()
            .Select(category =>
            {
                List<CaseOutcome> inCategory = outcomes.Where(o => o.Case.Category == category).ToList();
                return new CategoryTally(
                    category,
                    inCategory.Count(o => o.Verdict == CaseVerdict.Pass),
                    inCategory.Count(o => o.Verdict == CaseVerdict.Fail),
                    inCategory.Count(o => o.Verdict == CaseVerdict.Pending));
            })
            .Where(t => t.Total > 0)
            .ToList();

        List<CaseLine> cases = outcomes
            .Select(o => new CaseLine(
                o.Case.Id,
                o.Case.Category,
                o.Verdict,
                o.Result?.Attempts,
                o.Result?.CriticAttempts,
                o.Result?.Elapsed,
                o.Result?.TrackId,
                o.Notes,
                o.Result?.Outcome,
                o.Result?.FallbackReason,
                o.Case.ForceCriticRejections > 0 || o.Case.ForceValidationFailures > 0 || o.Case.ForceGenerateExceptions > 0,
                o.Result is null ? null : UsageLine.From(o.Result.LlmUsage, costEstimator)))
            .ToList();

        List<UsageLine> usage = UsageLine.From(
            outcomes.SelectMany(o => o.Result?.LlmUsage ?? []).ToList(), costEstimator);

        return new RunReport
        {
            RunAtUtc = DateTimeOffset.UtcNow,
            TrackStorageDirectory = trackStorageDirectory,
            ByCategory = byCategory,
            Cases = cases,
            Usage = usage
        };
    }
}

public sealed record CategoryTally(EvalCategory Category, int Pass, int Fail, int Pending)
{
    public int Total => Pass + Fail + Pending;
}

public sealed record CaseLine(
    string Id,
    EvalCategory Category,
    CaseVerdict Verdict,
    int? Attempts,
    int? CriticAttempts,
    TimeSpan? Elapsed,
    Guid? TrackId,
    IReadOnlyList<string> Notes,
    ActualOutcome? Outcome = null,
    FallbackReason? FallbackReason = null,
    bool FaultInjected = false,
    IReadOnlyList<UsageLine>? Usage = null)
{
    /// <summary>
    /// Estimated spend on this case: zero when it ran but made no LLM calls (e.g. the rule-based
    /// fallback after a simulated outage). Null when it has no result, or a model it called has no price.
    /// </summary>
    public decimal? CostUsd => Usage is null || Usage.Any(u => u.CostUsd is null)
        ? null
        : Usage.Sum(u => u.CostUsd!.Value);
}

/// <summary>Tokens and estimated cost for one step against one model and service tier.</summary>
public sealed record UsageLine(
    string Step, string Model, string ServiceTier, long Calls, long InputTokens, long OutputTokens, decimal? CostUsd)
{
    /// <summary>Merges usage that shares a step, model and tier (several cases, or repeated calls) and prices each group.</summary>
    public static List<UsageLine> From(IEnumerable<LlmUsage> usage, CostEstimator costEstimator) => usage
        .GroupBy(u => (u.Step, u.Model, u.ServiceTier))
        .Select(group =>
        {
            LlmUsage merged = new(
                group.Key.Step, group.Key.Model, group.Key.ServiceTier,
                group.Sum(u => u.Calls), group.Sum(u => u.InputTokens), group.Sum(u => u.OutputTokens));
            return new UsageLine(
                merged.Step, merged.Model, merged.ServiceTier,
                merged.Calls, merged.InputTokens, merged.OutputTokens, costEstimator.Estimate(merged));
        })
        .OrderBy(u => u.Step, StringComparer.Ordinal)
        .ThenBy(u => u.Model, StringComparer.Ordinal)
        .ToList();
}