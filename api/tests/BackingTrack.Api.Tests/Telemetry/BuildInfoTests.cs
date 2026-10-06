namespace BackingTrack.Api.Tests.Telemetry;

public sealed class BuildInfoTests
{
    [Fact]
    public void Version_ComesFromTheInformationalVersionAttribute()
    {
        // The attribute is always emitted by the SDK; "unknown" would mean the lookup silently
        // broke. The "+<sha>" suffix only exists on deploy builds, so it can't be asserted here.
        Assert.NotEqual("unknown", BuildInfo.Version);
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Version));
    }
}