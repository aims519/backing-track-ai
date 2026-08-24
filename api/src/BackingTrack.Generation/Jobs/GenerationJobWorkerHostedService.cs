using System.Diagnostics;
using System.Threading.Channels;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.TrackGeneration;

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
            
            using Activity? activity = GenerationJobActivitySource.Instance.StartActivity("GenerationJobWorkerHostedService.ExecuteAsync");
            activity?.SetTag("job.id", generationJob.Id);
            activity?.SetTag("job.mode", generationJob.Mode);

            GenerationJob updatedJob = generationJob with { State = JobStates.InProgress };
            jobStore.AddOrUpdate(updatedJob);
            logger.LogInformation("Job {Id} started", jobId);

            // Process the generation job here
            // Update the job state as needed
            using IServiceScope scope = serviceScopeFactory.CreateScope();
            
            #region Retrieval
            IRetrieveStep retrieveStep = scope.ServiceProvider.GetRequiredService<IRetrieveStep>();
            #region Style

            MatchedStyle? matchedStyle;
            try
            {
                matchedStyle = await retrieveStep.RetrieveTopMatchingStyles(updatedJob.Style, stoppingToken);
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
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
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
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                continue;
            }

            #endregion

            #region Scales

            IReadOnlyList<MatchedScale> matchingScaleEntries;
            try
            {
                matchingScaleEntries = retrieveStep.RetrieveMatchingScales(generationJob.KeyRoot,
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
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                continue;
            }
            
            if(!matchingScaleEntries.Any())
                logger.LogInformation("Job {Id} has found no matching scales",  jobId);
            else
                logger.LogInformation("Job {Id} found matching scales: {ScalesString}", jobId, 
                    string.Join(',', matchingScaleEntries.Select(s => $"{s.Chord.Root}{s.ScaleEntry.Id}/{s.Chord.Root}{s.Chord.ChordEntry.Id}")));

            #endregion
            #endregion

            #region Generation
            IGenerateStep generateStep = scope.ServiceProvider.GetRequiredService<IGenerateStep>();
            
            logger.LogInformation("Job {Id} generating a candidate track...", jobId);
            CandidateTrack candidateTrack;
            try
            {
                candidateTrack = await generateStep.GenerateCandidateTrackAsync(generationJob.KeyRoot,
                    generationJob.Tempo,
                    generationJob.TimeSignatureBeatCount,
                    generationJob.TimeSignatureBeatUnit,
                    matchedStyle, matchingChords,  matchingScaleEntries, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Service shutdown, not a job failure - let it propagate and stop the loop.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {Id} failed during candidate track generation", jobId);
                GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                jobStore.AddOrUpdate(failedJob);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                continue;
            }
            
            logger.LogInformation("Job {Id} generated a candidate track\nLengthInBeats: {BeatCount}\nDrumEventCount: {DrumEventCount}\nChordVoicingEventCount: {ChordVoicingEventCount}", 
                jobId, candidateTrack.LengthInBeats, candidateTrack.Drums.Count, candidateTrack.ChordVoicings.Count);
            
            #endregion

            GenerationJob completedJob = updatedJob with { State = JobStates.Complete, EndTime = DateTimeOffset.UtcNow };
            jobStore.AddOrUpdate(completedJob);

            logger.LogInformation("Job {Id} completed", jobId);
        }
    }
}