using BackingTrack.Generation.Eval.Model;
using BackingTrack.Generation.Eval.Reporting;
using BackingTrack.Generation.Eval.Running;
using BackingTrack.Generation.Steps.FallbackGeneration;

namespace BackingTrack.Generation.Eval.Tests.Reporting;

public class RunReportTests
{
    [Fact]
    public void OrganicFallbackRate_CountsOnlyCasesWithoutFaultInjection()
    {
        RunReport report = RunReport.From(
        [
            Outcome("organic-clean", ActualOutcome.Succeeded),
            Outcome("organic-fell-back", ActualOutcome.SucceededViaFallback, FallbackReason.ValidationExhausted),
            Outcome("forced-fallback", ActualOutcome.SucceededViaFallback, FallbackReason.GenerateError, forceGenerateExceptions: 1),
            Outcome("forced-critic", ActualOutcome.Succeeded, forceCriticRejections: 1)
        ]);

        Assert.Equal(2, report.OrganicCases);
        Assert.Equal(1, report.OrganicFallbacks);
        Assert.Equal(0.5, report.OrganicFallbackRate);
    }

    [Fact]
    public void OrganicFallbackRate_OnlyFaultInjectedCases_IsNull()
    {
        RunReport report = RunReport.From(
            [Outcome("forced", ActualOutcome.SucceededViaFallback, FallbackReason.CriticError, forceValidationFailures: 3)]);

        Assert.Null(report.OrganicFallbackRate);
    }

    [Fact]
    public void From_FallbackCase_RecordsOutcomeAndReasonOnTheCaseLine()
    {
        RunReport report = RunReport.From(
            [Outcome("fell-back", ActualOutcome.SucceededViaFallback, FallbackReason.RepairError)]);

        CaseLine line = Assert.Single(report.Cases);
        Assert.Equal(ActualOutcome.SucceededViaFallback, line.Outcome);
        Assert.Equal(FallbackReason.RepairError, line.FallbackReason);
        Assert.False(line.FaultInjected);
    }

    [Fact]
    public void From_WithPrices_CostsEachCaseAndTheRun()
    {
        EvalPricingSettings pricing = new();
        pricing.Models["model-a"] = new ModelPrice { InputPerMillionTokens = 1m, OutputPerMillionTokens = 4m };

        RunReport report = RunReport.From(
        [
            Outcome("one", ActualOutcome.Succeeded, usage: [new LlmUsage("generate", "model-a", "default", 1, 1_000_000, 250_000)]),
            Outcome("two", ActualOutcome.Succeeded, usage: [new LlmUsage("generate", "model-a", "default", 1, 500_000, 0)])
        ], costEstimator: new CostEstimator(pricing));

        Assert.Equal(2m, report.Cases[0].CostUsd);
        Assert.Equal(0.5m, report.Cases[1].CostUsd);
        Assert.Equal(2.5m, report.TotalCostUsd);

        UsageLine merged = Assert.Single(report.Usage);
        Assert.Equal(2, merged.Calls);
        Assert.Equal(1_500_000, merged.InputTokens);
    }

    [Fact]
    public void TotalCostUsd_AnyCalledModelUnpriced_IsNullNotPartial()
    {
        EvalPricingSettings pricing = new();
        pricing.Models["model-a"] = new ModelPrice { InputPerMillionTokens = 1m, OutputPerMillionTokens = 4m };

        RunReport report = RunReport.From(
        [
            Outcome("one", ActualOutcome.Succeeded, usage:
            [
                new LlmUsage("generate", "model-a", "default", 1, 1_000, 1_000),
                new LlmUsage("critic", "model-unpriced", "default", 1, 1_000, 1_000)
            ])
        ], costEstimator: new CostEstimator(pricing));

        Assert.Null(report.TotalCostUsd);
        Assert.Null(report.Cases[0].CostUsd);
        Assert.NotNull(report.Usage.Single(u => u.Model == "model-a").CostUsd);
    }

    [Fact]
    public void From_CaseThatRanWithoutAnyLlmCalls_CostsZero()
    {
        RunReport report = RunReport.From(
            [Outcome("llm-outage", ActualOutcome.SucceededViaFallback, FallbackReason.GenerateError, forceGenerateExceptions: 1)]);

        Assert.Equal(0m, report.Cases[0].CostUsd);
    }

    [Fact]
    public void From_CaseWithNoResult_HasNoUsageOrCost()
    {
        RunReport report = RunReport.From(
            [new CaseOutcome { Case = Case("threw"), Verdict = CaseVerdict.Fail, Notes = ["harness error"] }]);

        CaseLine line = Assert.Single(report.Cases);
        Assert.Null(line.CostUsd);
        Assert.Empty(report.Usage);
        Assert.Equal(0m, report.TotalCostUsd);
    }

    private static CaseOutcome Outcome(
        string id,
        ActualOutcome outcome,
        FallbackReason? fallbackReason = null,
        int forceCriticRejections = 0,
        int forceValidationFailures = 0,
        int forceGenerateExceptions = 0,
        IReadOnlyList<LlmUsage>? usage = null) => new()
        {
            Case = Case(id) with
            {
                ForceCriticRejections = forceCriticRejections,
                ForceValidationFailures = forceValidationFailures,
                ForceGenerateExceptions = forceGenerateExceptions
            },
            Verdict = CaseVerdict.Pass,
            Result = new GenerationRunResult
            {
                Outcome = outcome,
                FallbackReason = fallbackReason,
                LlmUsage = usage ?? [],
                Elapsed = TimeSpan.Zero
            }
        };

    private static EvalCase Case(string id) => new()
    {
        Id = id,
        Category = EvalCategory.Baseline,
        Description = "test",
        Input = new GenerationInput { Tempo = 120, Chords = ["E", "A"], KeyRoot = "E", Style = "rock" },
        Expect = new CaseExpectations { Outcome = ExpectedOutcome.Succeeds }
    };
}