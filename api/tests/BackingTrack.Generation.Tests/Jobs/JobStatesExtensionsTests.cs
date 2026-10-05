using BackingTrack.Generation.Jobs;

namespace BackingTrack.Generation.Tests.Jobs;

public class JobStatesExtensionsTests
{
    // Tag values are a dashboard contract (see GenerationJobMetrics.RecordOutcome), so they're pinned here
    // rather than derived from the enum names.
    [Theory]
    [InlineData(JobStates.Pending, "pending")]
    [InlineData(JobStates.InProgress, "in_progress")]
    [InlineData(JobStates.Complete, "complete")]
    [InlineData(JobStates.CompletedViaFallback, "completed_via_fallback")]
    [InlineData(JobStates.Failed, "failed")]
    [InlineData(JobStates.Cancelled, "cancelled")]
    public void ToMetricTag_ReturnsTheFixedTagValueForEachState(JobStates state, string expected)
    {
        Assert.Equal(expected, state.ToMetricTag());
    }

    [Fact]
    public void ToMetricTag_UnknownState_Throws()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => ((JobStates)999).ToMetricTag());

        Assert.Equal("state", ex.ParamName);
    }
}