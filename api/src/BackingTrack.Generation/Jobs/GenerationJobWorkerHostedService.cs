using System.Threading.Channels;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Jobs;

public class GenerationJobWorkerHostedService(JobStore jobStore, GenerationJobQueue jobQueue, ILogger<GenerationJobWorkerHostedService> logger, IRetrieveStep retrieveStep) : BackgroundService
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
            StyleEntry? matchingStyleEntry;
            try
            {
                matchingStyleEntry = await retrieveStep.RetrieveTopMatchingStyleAsync(updatedJob.Style, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Service shutdown, not a job failure - let it propagate and stop the loop.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {Id} failed during style retrieval", jobId);
                GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                jobStore.AddOrUpdate(failedJob);
                continue;
            }

            if (matchingStyleEntry == null)
            {
                logger.LogWarning("Job {Id} found no matching style entry for style '{Style}'", jobId, updatedJob.Style);
                GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                jobStore.AddOrUpdate(failedJob);
                continue;
            }

            logger.LogInformation("Job {Id} found matching style entry, {Style}", jobId, matchingStyleEntry.Id);

            GenerationJob completedJob = updatedJob with { State = JobStates.Complete, EndTime = DateTimeOffset.UtcNow };
            jobStore.AddOrUpdate(completedJob);

            logger.LogInformation("Job {Id} completed", jobId);
        }
    }
}