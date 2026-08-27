using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Executors;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BackingTrack.Generation.Tests.Workflows.Executors;

public class GenerateCandidateTrackExecutorTests
{
    private static GenerateCandidateTrackExecutor CreateExecutor(Mock<IGenerateStep> generateStepMock) =>
        new(generateStepMock.Object, NullLogger<GenerateCandidateTrackExecutor>.Instance);

    [Fact]
    public async Task HandleAsync_TrackGenerated_SendsValidateCandidateTrackInputStartingAtAttemptOne()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        MatchedScale scale = WorkflowTestFixtures.CreateMatchedScale(chord);
        CandidateTrack candidateTrack = new(16, [], [], []);
        Mock<IGenerateStep> generateStepMock = new();
        generateStepMock.Setup(s => s.GenerateCandidateTrackAsync(job.KeyRoot, job.Tempo, job.TimeSignatureBeatCount,
                job.TimeSignatureBeatUnit, matchedStyle, It.IsAny<IReadOnlyList<MatchedChord>>(), It.IsAny<IReadOnlyList<MatchedScale>>(),
                null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidateTrack);
        FakeWorkflowContext context = new();

        await CreateExecutor(generateStepMock).HandleAsync(
            new GenerateCandidateTrackInput(job, matchedStyle, [chord], [scale]), context, CancellationToken.None);

        ValidateCandidateTrackInput sent = Assert.IsType<ValidateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(candidateTrack, sent.CandidateTrack);
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal(1, sent.Attempts);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_GenerateStepThrows_YieldsGenerationFailedAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        MatchedScale scale = WorkflowTestFixtures.CreateMatchedScale(chord);
        Mock<IGenerateStep> generateStepMock = new();
        generateStepMock.Setup(s => s.GenerateCandidateTrackAsync(job.KeyRoot, job.Tempo, job.TimeSignatureBeatCount,
                job.TimeSignatureBeatUnit, matchedStyle, It.IsAny<IReadOnlyList<MatchedChord>>(), It.IsAny<IReadOnlyList<MatchedScale>>(),
                null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("generation failed"));
        FakeWorkflowContext context = new();

        await CreateExecutor(generateStepMock).HandleAsync(
            new GenerateCandidateTrackInput(job, matchedStyle, [chord], [scale]), context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal("generation failed", failed.Reason);
        Assert.Empty(context.SentMessages);
    }
}
