using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

using BackingTrack.Core.Config;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Retrieval;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackRepair;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Jobs;

public class GenerationJobWorkerHostedService(JobStore jobStore, 
    GenerationJobQueue jobQueue, 
    ILogger<GenerationJobWorkerHostedService> logger, 
    IServiceScopeFactory serviceScopeFactory,
    IOptions<TrackStorageSettings> trackStorageSettings,
    IOptions<TrackGenerationWorkflowSettings> trackGenerationWorkflowSettings) : BackgroundService
{
    private readonly ChannelReader<Guid> _jobReader = jobQueue.Reader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Guid jobId in _jobReader.ReadAllAsync(stoppingToken))
        {
            await ProcessGenerationJobAsync(jobId, trackGenerationWorkflowSettings.Value.GenerationMaxAttempts, stoppingToken);
        }
    }

    private async Task ProcessGenerationJobAsync(Guid jobId, int maxGenerationAttempts, CancellationToken stoppingToken)
    {
            GenerationJob? generationJob = jobStore.TryGet(jobId);
            if (generationJob == null)
            {
                logger.LogInformation("Job {Id} does not exist", jobId);
                return;
            }
            
            using Activity? activity = GenerationJobActivitySource.Instance.StartActivity($"{nameof(GenerationJobWorkerHostedService)}.ProcessGenerationJobAsync");
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
                return;
            }

            if (matchedStyle == null)
            {
                logger.LogWarning("Job {Id} found no matching style entry for style '{Style}'", jobId, updatedJob.Style);
                GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                jobStore.AddOrUpdate(failedJob);
                return;
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
                return;
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
                return;
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
                    matchedStyle, matchingChords,  matchingScaleEntries, 
                    previousAttempt: null,
                    previousAttemptFailures: null,
                    stoppingToken);
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
                return;
            }
            
            logger.LogInformation("Job {Id} generated a candidate track\nLengthInBeats: {BeatCount}\nDrumEventCount: {DrumEventCount}\nBassEventCount: {BassEventCount}\nChordVoicingEventCount: {ChordVoicingEventCount}",
                    jobId, candidateTrack.LengthInBeats, candidateTrack.Drums.Count, candidateTrack.Bass.Count, candidateTrack.ChordVoicings.Count);
            #endregion

            #region Validation and Repair

            IValidateStep validateStep = scope.ServiceProvider.GetRequiredService<IValidateStep>();
            IRepairStep  repairStep = scope.ServiceProvider.GetRequiredService<IRepairStep>();

            TrackValidationResult? trackValidationResult = null;
            for (int attempt = 1; attempt <= maxGenerationAttempts; attempt++)
            {
                trackValidationResult = validateStep.ValidateCandidateTrack(candidateTrack, generationJob, matchingChords, matchingScaleEntries);
                if (trackValidationResult.IsValid) 
                    break;
                
                logger.LogDebug("Job {Id} attempt {Attempt} candidate track: {Track}", jobId, attempt,
                    JsonSerializer.Serialize(candidateTrack, new JsonSerializerOptions { TypeInfoResolver = CandidateTrackJsonContext.Default }));
                logger.LogWarning("Job {Id} attempt {Attempt} failed validation: {Errors}", jobId, attempt, string.Join('\n', trackValidationResult.Failures.Select(f => f.Reason)));
                
                if(attempt == maxGenerationAttempts)
                    break;
                
                try
                {
                    candidateTrack = await repairStep.RepairCandidateTrackAsync(candidateTrack, 
                        trackValidationResult.Failures, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Service shutdown, not a job failure - let it propagate and stop the loop.
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Job {Id} failed during candidate track generation on retry attempt {Attempt}", jobId, attempt);
                    GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                    jobStore.AddOrUpdate(failedJob);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    return;
                }
            }

            if (trackValidationResult is null || !trackValidationResult.IsValid)
            {
                GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                jobStore.AddOrUpdate(failedJob);
                activity?.SetStatus(ActivityStatusCode.Error, $"Valid track could not be generated after {maxGenerationAttempts} attempts");
                logger.LogError("Job {Id} track generation exhausted all {Attempts} attempts and failed validation.\n{Errors}", generationJob.Id, maxGenerationAttempts, string.Join('\n', trackValidationResult?.Failures.Select(f => f.Reason) ?? Array.Empty<string>()));
                return;
            }
            
            #endregion
            
            #region Persistance
            Guid trackId = Guid.NewGuid();
            try
            {
                Directory.CreateDirectory(trackStorageSettings.Value.BaseFolderPath);
                string trackJson = JsonSerializer.Serialize(candidateTrack, CandidateTrackJsonContext.Default.CandidateTrack);
                string trackFilePath = Path.Combine(trackStorageSettings.Value.BaseFolderPath, $"{trackId}.json");
                await File.WriteAllTextAsync(trackFilePath, trackJson, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Service shutdown, not a job failure - let it propagate and stop the loop.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job {Id} failed to persist the candidate track", jobId);
                GenerationJob failedJob = updatedJob with { State = JobStates.Failed, EndTime = DateTimeOffset.UtcNow };
                jobStore.AddOrUpdate(failedJob);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return;
            }
            #endregion

            logger.LogDebug("Job {Id} completed track saved: {Track}", generationJob.Id, JsonSerializer.Serialize(candidateTrack, new JsonSerializerOptions { TypeInfoResolver = CandidateTrackJsonContext.Default }));
            
            GenerationJob completedJob = updatedJob with { State = JobStates.Complete, EndTime = DateTimeOffset.UtcNow, TrackId = trackId };
            jobStore.AddOrUpdate(completedJob);

            logger.LogInformation("Job {Id} completed", jobId);
    }
}