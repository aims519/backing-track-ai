using System.Threading.Channels;
using BackingTrack.Generation.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation;

public class GenerationWorkerHostedService(JobStore jobStore, GenerationJobQueue jobQueue, ILogger<GenerationWorkerHostedService> logger) : BackgroundService
{
    private readonly ChannelReader<Guid> _jobReader = jobQueue.Reader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Guid jobId in _jobReader.ReadAllAsync(stoppingToken))
        {
            GenerationJob? generationJob = jobStore.TryGet(jobId);
            if (generationJob == null)
            {
                logger.LogInformation("Job {Id} does not exist", jobId);
                continue;
            }

            GenerationJob updatedJob = generationJob with { State = JobStates.InProgress };
            jobStore.AddOrUpdate(updatedJob);
            logger.LogInformation("Job {Id} started", jobId);

            // Process the generation job here
            // Update the job state as needed
            await Task.Delay(3000, stoppingToken);
            GenerationJob completedJob = updatedJob with { State = JobStates.Complete, EndTime = DateTimeOffset.UtcNow };
            jobStore.AddOrUpdate(completedJob);

            logger.LogInformation("Job {Id} completed", jobId);
        }
    }
}