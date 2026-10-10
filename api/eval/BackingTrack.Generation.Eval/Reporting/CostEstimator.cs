using BackingTrack.Generation.Eval.Running;

namespace BackingTrack.Generation.Eval.Reporting;

/// <summary>Per-model list prices, bound from the <c>Pricing</c> section of <c>appsettings.json</c>.</summary>
public sealed class EvalPricingSettings
{
    /// <summary>Standard-tier list price by model id, in USD per million tokens.</summary>
    public Dictionary<string, ModelPrice> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Multiplier applied to the standard price for a service tier, keyed by the tier tag the steps emit
    /// (<c>flex</c>, <c>default</c>, ...). A tier with no entry is billed at the standard price.
    /// </summary>
    public Dictionary<string, decimal> ServiceTierMultipliers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelPrice
{
    public decimal InputPerMillionTokens { get; set; }
    public decimal OutputPerMillionTokens { get; set; }
}

/// <summary>
/// Turns token counts into USD. A model with no configured price yields <c>null</c> rather than zero,
/// so a missing price shows up as a gap in the report instead of silently understating cost.
/// </summary>
public sealed class CostEstimator(EvalPricingSettings pricing)
{
    private const decimal TokensPerPriceUnit = 1_000_000m;

    public static CostEstimator None { get; } = new(new EvalPricingSettings());

    public decimal? Estimate(LlmUsage usage)
    {
        if (!pricing.Models.TryGetValue(usage.Model, out ModelPrice? price))
            return null;

        decimal multiplier = pricing.ServiceTierMultipliers.TryGetValue(usage.ServiceTier, out decimal tierMultiplier)
            ? tierMultiplier
            : 1m;

        return (usage.InputTokens * price.InputPerMillionTokens + usage.OutputTokens * price.OutputPerMillionTokens)
            / TokensPerPriceUnit * multiplier;
    }
}