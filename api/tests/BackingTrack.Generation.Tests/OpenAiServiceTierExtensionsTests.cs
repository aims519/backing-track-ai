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
    // rather than derived from the enum names. Unset is reported separately from an explicit Auto: with no tier sent, OpenAI falls back to the project default.
    public static TheoryData<OpenAiServiceTier?, string> MetricTags => new()
    {
        { null, "not_specified" },
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

    [Fact]
    public void ToMetricTag_UnknownTier_ThrowsNamingTheParameterAndValue()
    {
        OpenAiServiceTier? unknown = (OpenAiServiceTier)999;

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => unknown.ToMetricTag());

        Assert.Equal("serviceTier", ex.ParamName);
        Assert.Equal((OpenAiServiceTier)999, ex.ActualValue);
    }
#pragma warning restore OPENAI001
}