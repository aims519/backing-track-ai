using System.Text.Json;

using BackingTrack.Generation.Eval.Model;
using BackingTrack.Generation.Eval.Reporting;
using BackingTrack.Generation.Eval.Running;
using BackingTrack.Generation.Steps.FallbackGeneration;

namespace BackingTrack.Generation.Eval.Tests.Reporting;

public class JsonReportWriterTests
{
    [Fact]
    public async Task WriteAsync_FallbackCaseWithUsage_WritesOutcomeReasonUsageAndRunTotals()
    {
        EvalPricingSettings pricing = new();
        pricing.Models["model-a"] = new ModelPrice { InputPerMillionTokens = 1m, OutputPerMillionTokens = 2m };
        CaseOutcome outcome = new()
        {
            Case = new EvalCase
            {
                Id = "fell-back",
                Category = EvalCategory.Baseline,
                Description = "test",
                Input = new GenerationInput { Tempo = 120, Chords = ["E", "A"], KeyRoot = "E", Style = "rock" },
                Expect = new CaseExpectations { Outcome = ExpectedOutcome.Succeeds }
            },
            Verdict = CaseVerdict.Pass,
            Result = new GenerationRunResult
            {
                Outcome = ActualOutcome.SucceededViaFallback,
                FallbackReason = FallbackReason.ValidationExhausted,
                LlmUsage = [new LlmUsage("generate", "model-a", "flex", 1, 1_000_000, 500_000)],
                Elapsed = TimeSpan.FromSeconds(2)
            }
        };
        string path = Path.Combine(Path.GetTempPath(), $"eval-report-{Guid.NewGuid():N}.json");

        try
        {
            await JsonReportWriter.WriteAsync(path, RunReport.From([outcome], costEstimator: new CostEstimator(pricing)), TestContext.Current.CancellationToken);

            using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            JsonElement root = json.RootElement;
            JsonElement line = root.GetProperty("Cases")[0];

            Assert.Equal("SucceededViaFallback", line.GetProperty("Outcome").GetString());
            Assert.Equal("ValidationExhausted", line.GetProperty("FallbackReason").GetString());
            Assert.False(line.GetProperty("FaultInjected").GetBoolean());
            Assert.Equal(2m, line.GetProperty("CostUsd").GetDecimal());

            JsonElement usage = root.GetProperty("Usage")[0];
            Assert.Equal("model-a", usage.GetProperty("Model").GetString());
            Assert.Equal(1_000_000, usage.GetProperty("InputTokens").GetInt64());

            Assert.Equal(1.0, root.GetProperty("OrganicFallbackRate").GetDouble());
            Assert.Equal(2m, root.GetProperty("TotalCostUsd").GetDecimal());
        }
        finally
        {
            File.Delete(path);
        }
    }
}