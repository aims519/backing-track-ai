using BackingTrack.Generation.Eval.Model;
using BackingTrack.Generation.Eval.Running;

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

    public static RunReport From(IReadOnlyList<CaseOutcome> outcomes, string? trackStorageDirectory = null)
    {
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
                o.Notes))
            .ToList();

        return new RunReport
        {
            RunAtUtc = DateTimeOffset.UtcNow,
            TrackStorageDirectory = trackStorageDirectory,
            ByCategory = byCategory,
            Cases = cases
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
    IReadOnlyList<string> Notes);