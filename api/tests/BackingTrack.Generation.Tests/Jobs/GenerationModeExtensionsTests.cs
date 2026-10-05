using BackingTrack.Generation.Jobs;

namespace BackingTrack.Generation.Tests.Jobs;

public class GenerationModeExtensionsTests
{
    // Tag values are a dashboard contract (see ADR 0044), so they're pinned here rather than derived from
    // the enum names: renaming a GenerationMode member must not start a new series.
    [Theory]
    [InlineData(GenerationMode.Precise, "precise")]
    [InlineData(GenerationMode.Vibe, "vibe")]
    public void ToMetricTag_ReturnsTheFixedTagValueForEachMode(GenerationMode mode, string expected)
    {
        Assert.Equal(expected, mode.ToMetricTag());
    }

    [Fact]
    public void ToMetricTag_UnknownMode_Throws()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => ((GenerationMode)999).ToMetricTag());

        Assert.Equal("mode", ex.ParamName);
    }
}