using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Workflows.Executors;

internal sealed class GenerateCandidateTrackExecutor(
    IGenerateStep generateStep,
    IJobStatusEmitter statusEmitter,
    ILogger<GenerateCandidateTrackExecutor> logger) : Executor("GenerateCandidateTrackExecutor")
{
    internal async ValueTask HandleAsync(GenerateCandidateTrackInput input, IWorkflowContext context,
        CancellationToken cancellationToken)
    {
        GenerationJob generationJob = input.GenerationJob;
        Guid jobId = input.GenerationJob.Id;

        statusEmitter.Emit(jobId, "Generation", input.CriticAttempts,
            $"Generating {generationJob.Chords.Count} bar candidate track in {generationJob.KeyRoot} {generationJob.KeyMode}...");

        CandidateTrack candidateTrack;
        try
        {
            if (input.CriticReview is null && input.PreviousAttempt is null)
            {
                logger.LogInformation("Job {Id} generating a candidate track...", jobId);
                candidateTrack = await generateStep.GenerateCandidateTrackAsync(generationJob.KeyRoot,
                    generationJob.KeyMode,
                    generationJob.Tempo,
                    generationJob.TimeSignatureBeatCount,
                    generationJob.TimeSignatureBeatUnit,
                    input.MatchedStyle, input.MatchedChords, input.MatchedScales,
                    cancellationToken);
            }
            else
            {
                logger.LogInformation("Job {Id} generating a candidate track after failed critic review...", jobId);
                candidateTrack = await generateStep.GenerateCandidateTrackWithCriticReviewAsync(generationJob.KeyRoot,
                    generationJob.KeyMode,
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
            logger.LogInformation("Cancellation requested during {ExecutorName}", nameof(GenerateCandidateTrackExecutor));
            await context.YieldOutputAsync(new GenerationCancelled(), CancellationToken.None);
            return;
        }
        catch (Exception ex) // Unexpected errors should not fail the job so we fall back to rule-based generation (ADR 0034's "never fail" guarantee)
        {
            logger.LogError(ex, "Job {Id} failed during candidate track generation. Falling back to rule-based generation", jobId);
            await context.SendMessageAsync(new FallbackGenerateCandidateTrackInput(generationJob, input.MatchedStyle, input.MatchedChords, input.MatchedScales, FallbackReason.GenerateError), cancellationToken);
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
            .SendsMessage<FallbackGenerateCandidateTrackInput>()
            .YieldsOutput<GenerationOutcome>();
}