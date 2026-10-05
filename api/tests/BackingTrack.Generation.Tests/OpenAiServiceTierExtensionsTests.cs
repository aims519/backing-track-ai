using BackingTrack.Core.Config;

using Microsoft.Extensions.AI;

using OpenAI.Responses;

namespace BackingTrack.Generation.Tests;

public class OpenAiServiceTierExtensionsTests
{
#pragma warning disable OPENAI001 // Responses-API service tier is still evaluation-only upstream.
    [Fact]
    public void ApplyServiceTier_TierIsNull_LeavesRawRepresentationFactoryUnset()
    {
        ChatOptions options = new();

        options.ApplyServiceTier(null);

        Assert.Null(options.RawRepresentationFactory);
    }

    public static TheoryData<OpenAiServiceTier, ResponseServiceTier> ServiceTierMappings => new()
    {
        { OpenAiServiceTier.Auto, ResponseServiceTier.Auto },
        { OpenAiServiceTier.Default, ResponseServiceTier.Default },
        { OpenAiServiceTier.Flex, ResponseServiceTier.Flex },
        { OpenAiServiceTier.Scale, ResponseServiceTier.Scale },
    };

    [Theory]
    [MemberData(nameof(ServiceTierMappings))]
    public void ApplyServiceTier_TierIsSet_RawRepresentationProducesMatchingCreateResponseOptions(
        OpenAiServiceTier settingsTier, ResponseServiceTier expected)
    {
        ChatOptions options = new();

        options.ApplyServiceTier(settingsTier);

        Assert.NotNull(options.RawRepresentationFactory);
        CreateResponseOptions? raw = options.RawRepresentationFactory.Invoke(null!) as CreateResponseOptions;
        Assert.NotNull(raw);
        Assert.Equal(expected, raw.ServiceTier);
    }

    // Tag values are a dashboard contract (see GenerationJobMetrics.RecordOutcome), so they're pinned here
    // rather than derived from the enum names. Unset means OpenAI applies its own default, "auto".
    public static TheoryData<OpenAiServiceTier?, string> MetricTags => new()
    {
        { null, "auto" },
        { OpenAiServiceTier.Auto, "auto" },
        { OpenAiServiceTier.Default, "default" },
        { OpenAiServiceTier.Flex, "flex" },
        { OpenAiServiceTier.Scale, "scale" },
    };

    [Theory]
    [MemberData(nameof(MetricTags))]
    public void ToMetricTag_ReturnsTheFixedTagValueForEachTier(OpenAiServiceTier? tier, string expected)
    {
        Assert.Equal(expected, tier.ToMetricTag());
    }
#pragma warning restore OPENAI001
}