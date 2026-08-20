using System.Threading.Channels;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Jobs;

public class GenerationJobWorkerHostedService(JobStore jobStore, GenerationJobQueue jobQueue, ILogger<GenerationJobWorkerHostedService> logger, IServiceScopeFactory serviceScopeFactory) : BackgroundService
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
            using IServiceScope scope = serviceScopeFactory.CreateScope();
            IRetrieveStep retrieveStep = scope.ServiceProvider.GetRequiredService<IRetrieveStep>();
            
            #region Style

            MatchedStyle? matchedStyle;
            try
            {
                matchedStyle = await retrieveStep.RetrieveTopMatchingStyleAsync(updatedJob.Style, stoppingToken);
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

            if (matchedStyle == null)
            {
                logger.LogWarning("Job {Id} found no matching style entry for style '{Style}'", jobId, updatedJob.Style);
                GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                jobStore.AddOrUpdate(failedJob);
                continue;
            }

            logger.LogInformation("Job {Id} found matching style entry, {Style}", jobId, matchedStyle.StyleEntry.Id);

            #endregion

            #region Chords

            IReadOnlyList<MatchedChord> matchingChords;
            try
            {
                matchingChords = retrieveStep.RetrieveMatchingChords(generationJob.Chords);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {Id} failed during chord retrieval", jobId);
                GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                jobStore.AddOrUpdate(failedJob);
                continue;
            }

            #endregion

            #region Scales

            IReadOnlyList<ScaleEntry> matchingScaleEntries;
            try
            {
                matchingScaleEntries = retrieveStep.RetrieveMatchingScaleEntries(generationJob.Key,
                    matchingChords,
                    stoppingToken)
                    .ToList();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Service shutdown, not a job failure - let it propagate and stop the loop.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {Id} failed during scale retrieval", jobId);
                GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                jobStore.AddOrUpdate(failedJob);
                continue;
            }
            
            if(!matchingScaleEntries.Any())
                logger.LogInformation("Job {Id} has found no matching scales",  jobId);
            else
                logger.LogInformation("Job {Id} found matching scales: {ScalesString}", jobId, 
                    string.Join(',', matchingScaleEntries.Select(s => s.Id)));

            #endregion

            GenerationJob completedJob = updatedJob with { State = JobStates.Complete, EndTime = DateTimeOffset.UtcNow };
            jobStore.AddOrUpdate(completedJob);

            logger.LogInformation("Job {Id} completed", jobId);
        }
    }
}