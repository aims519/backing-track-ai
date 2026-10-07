using BackingTrack.Generation.Steps.FallbackGeneration;

namespace BackingTrack.Generation.Tests.Steps.FallbackGeneration;

public class FallbackReasonExtensionsTests
{
    // Tag values are a dashboard contract (see GenerationJobMetrics.RecordOutcome), so they're pinned here
    // rather than derived from the enum names.
    [Theory]
    [InlineData(FallbackReason.GenerateError, "generate_error")]
    [InlineData(FallbackReason.ValidationExhausted, "validation_exhausted")]
    [InlineData(FallbackReason.CriticExhausted, "critic_exhausted")]
    [InlineData(FallbackReason.RepairError, "repair_error")]
    [InlineData(FallbackReason.CriticError, "critic_error")]
    public void ToMetricTag_ReturnsTheFixedTagValueForEachReason(FallbackReason reason, string expected)
    {
        Assert.Equal(expected, reason.ToMetricTag());
    }

    [Fact]
    public void ToMetricTag_UnknownReason_Throws()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => ((FallbackReason)999).ToMetricTag());

        Assert.Equal("fallbackReason", ex.ParamName);
    }
}