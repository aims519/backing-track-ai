using BackingTrack.Generation.Eval.Reporting;
using BackingTrack.Generation.Eval.Running;

namespace BackingTrack.Generation.Eval.Tests.Reporting;

public class CostEstimatorTests
{
    [Fact]
    public void Estimate_PricedModelAtStandardTier_MultipliesTokensByPerMillionPrice()
    {
        CostEstimator estimator = new(Pricing(("model-a", 2m, 10m)));

        decimal? cost = estimator.Estimate(Usage("model-a", "default", input: 500_000, output: 100_000));

        Assert.Equal(2m, cost); // 0.5M * $2 + 0.1M * $10
    }

    [Fact]
    public void Estimate_TierWithMultiplier_ScalesTheStandardPrice()
    {
        EvalPricingSettings pricing = Pricing(("model-a", 2m, 10m));
        pricing.ServiceTierMultipliers["flex"] = 0.5m;
        CostEstimator estimator = new(pricing);

        decimal? cost = estimator.Estimate(Usage("model-a", "flex", input: 500_000, output: 100_000));

        Assert.Equal(1m, cost);
    }

    [Fact]
    public void Estimate_TierWithoutMultiplier_IsBilledAtStandardPrice()
    {
        EvalPricingSettings pricing = Pricing(("model-a", 2m, 10m));
        pricing.ServiceTierMultipliers["flex"] = 0.5m;
        CostEstimator estimator = new(pricing);

        decimal? cost = estimator.Estimate(Usage("model-a", "not_specified", input: 1_000_000, output: 0));

        Assert.Equal(2m, cost);
    }

    [Fact]
    public void Estimate_ModelWithoutPrice_ReturnsNullRatherThanZero()
    {
        CostEstimator estimator = new(Pricing(("model-a", 2m, 10m)));

        Assert.Null(estimator.Estimate(Usage("model-b", "default", input: 1_000, output: 1_000)));
    }

    [Fact]
    public void Estimate_ModelLookupIgnoresCase()
    {
        CostEstimator estimator = new(Pricing(("Model-A", 2m, 10m)));

        Assert.NotNull(estimator.Estimate(Usage("model-a", "default", input: 1_000, output: 1_000)));
    }

    private static EvalPricingSettings Pricing(params (string Model, decimal Input, decimal Output)[] models)
    {
        EvalPricingSettings pricing = new();
        foreach ((string model, decimal input, decimal output) in models)
            pricing.Models[model] = new ModelPrice { InputPerMillionTokens = input, OutputPerMillionTokens = output };
        return pricing;
    }

    private static LlmUsage Usage(string model, string tier, long input, long output) =>
        new("generate", model, tier, Calls: 1, input, output);
}