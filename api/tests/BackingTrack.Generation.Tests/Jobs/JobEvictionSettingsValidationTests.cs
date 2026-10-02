using BackingTrack.Generation.Jobs;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BackingTrack.Generation.Tests.Jobs;

// Builds a real host around AddJobProcessing, because ValidateOnStart only does anything when the host
// starts - resolving IOptions directly wouldn't prove the startup check is wired.
public class JobEvictionSettingsValidationTests
{
    private static IHost BuildHost(Dictionary<string, string?> config)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(config);
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        builder.Services.AddJobProcessing(builder.Configuration);
        return builder.Build();
    }

    [Fact]
    public async Task StartAsync_ValidSettings_StartsAndStops()
    {
        using IHost host = BuildHost(new()
        {
            ["JobEviction:SweepIntervalSeconds"] = "60",
            ["JobEviction:JobTtlSeconds"] = "3600",
        });

        await host.StartAsync(TestContext.Current.CancellationToken);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("0", "3600", "SweepIntervalSeconds")]
    [InlineData("-5", "3600", "SweepIntervalSeconds")]
    [InlineData("60", "0", "JobTtlSeconds")]
    [InlineData("60", "-1", "JobTtlSeconds")]
    public async Task StartAsync_NonPositiveSetting_FailsStartupNamingTheProperty(string sweepInterval, string jobTtl, string expectedProperty)
    {
        using IHost host = BuildHost(new()
        {
            ["JobEviction:SweepIntervalSeconds"] = sweepInterval,
            ["JobEviction:JobTtlSeconds"] = jobTtl,
        });

        OptionsValidationException ex = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains(expectedProperty, ex.Message);
    }

    [Fact]
    public async Task StartAsync_SectionMissing_FailsStartupInsteadOfCrashingThePeriodicTimer()
    {
        // Unbound settings default to 0, which previously reached `new PeriodicTimer(TimeSpan.Zero)` and
        // took the host down with an ArgumentOutOfRangeException rather than a readable options error.
        using IHost host = BuildHost([]);

        await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));
    }
}