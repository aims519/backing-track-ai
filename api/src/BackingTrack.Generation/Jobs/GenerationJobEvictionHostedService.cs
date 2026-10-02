using BackingTrack.Core.Config;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Jobs;

public class GenerationJobEvictionHostedService(JobStore jobStore,
    TimeProvider timeProvider,
    IOptions<JobEvictionSettings> settings,
    ILogger<GenerationJobEvictionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(settings.Value.SweepIntervalSeconds), timeProvider);
        int jobTtlSeconds = settings.Value.JobTtlSeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int evictedCount = jobStore.EvictTerminatedJobs(timeProvider.GetUtcNow() - TimeSpan.FromSeconds(jobTtlSeconds));
                if (evictedCount > 0)
                    logger.LogInformation("Evicted {Count} stale jobs", evictedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while evicting stale jobs");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}