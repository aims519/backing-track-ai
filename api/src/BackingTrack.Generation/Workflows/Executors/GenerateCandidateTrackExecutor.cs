using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class GenerateCandidateTrackExecutor(
    IGenerateStep generateStep,
    ILogger<GenerateCandidateTrackExecutor> logger) : Executor("GenerateCandidateTrackExecutor")
{
    internal async ValueTask HandleAsync(GenerateCandidateTrackInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        GenerationJob generationJob = input.GenerationJob;
        Guid jobId = input.GenerationJob.Id;

        CandidateTrack candidateTrack;
        try
        {
            if (input.CriticReview is null && input.PreviousAttempt is null)
            {
                logger.LogInformation("Job {Id} generating a candidate track...", jobId);
                candidateTrack = await generateStep.GenerateCandidateTrackAsync(generationJob.KeyRoot,
                    generationJob.Tempo,
                    generationJob.TimeSignatureBeatCount,
                    generationJob.TimeSignatureBeatUnit,
                    input.MatchedStyle, input.MatchedChords, input.MatchedScales,
                    previousAttempt: null,
                    previousAttemptFailures: null,
                    cancellationToken);
            }
            else
            {
                logger.LogInformation("Job {Id} generating a candidate track after failed critic review...", jobId);
                candidateTrack = await generateStep.GenerateCandidateTrackWithCriticReviewAsync(generationJob.KeyRoot,
                    generationJob.Tempo,
                    generationJob.TimeSignatureBeatCount,
                    generationJob.TimeSignatureBeatUnit,
                    input.MatchedStyle, input.MatchedChords, input.MatchedScales,
                    previousAttempt: input.PreviousAttempt!,
                    criticReview: input.CriticReview!,
                    cancellationToken);
            }

        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Service shutdown, not a job failure
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {Id} failed during candidate track generation", jobId);
            await context.YieldOutputAsync(new GenerationFailed(ex.Message), cancellationToken);
            return;
        }

        logger.LogInformation("Job {Id} generated a candidate track\nLengthInBeats: {BeatCount}\nDrumEventCount: {DrumEventCount}\nBassEventCount: {BassEventCount}\nChordVoicingEventCount: {ChordVoicingEventCount}",
            jobId, candidateTrack.LengthInBeats, candidateTrack.Drums.Count, candidateTrack.Bass.Count, candidateTrack.ChordVoicings.Count);
        await context.SendMessageAsync(new ValidateCandidateTrackInput(candidateTrack, generationJob, input.MatchedStyle, input.MatchedChords, input.MatchedScales, 1, input.CriticAttempts), cancellationToken);
    }

    protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
        protocolBuilder
            .ConfigureRoutes(routes => routes.AddHandler<GenerateCandidateTrackInput>(HandleAsync))
            .SendsMessage<ValidateCandidateTrackInput>()
            .YieldsOutput<GenerationOutcome>();
}