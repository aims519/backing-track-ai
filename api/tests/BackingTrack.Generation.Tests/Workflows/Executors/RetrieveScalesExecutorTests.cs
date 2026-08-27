using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Retrieval;
using BackingTrack.Generation.Workflows.Executors;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BackingTrack.Generation.Tests.Workflows.Executors;

public class RetrieveScalesExecutorTests
{
    private static RetrieveScalesExecutor CreateExecutor(Mock<IRetrieveStep> retrieveStepMock) =>
        new(retrieveStepMock.Object, NullLogger<RetrieveScalesExecutor>.Instance);

    [Fact]
    public async Task HandleAsync_ScalesMatched_SendsGenerateCandidateTrackInputPreservingStyleAndChords()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        MatchedScale[] matchedScales = [WorkflowTestFixtures.CreateMatchedScale(chord)];
        Mock<IRetrieveStep> retrieveStepMock = new();
        retrieveStepMock.Setup(s => s.RetrieveMatchingScales(job.KeyRoot, It.IsAny<IReadOnlyList<MatchedChord>>(), It.IsAny<CancellationToken>()))
            .Returns(matchedScales);
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock).HandleAsync(
            new RetrieveScalesInput([chord], matchedStyle, job), context, CancellationToken.None);

        GenerateCandidateTrackInput sent = Assert.IsType<GenerateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(job, sent.GenerationJob);
        Assert.Same(matchedStyle, sent.MatchedStyle);
        Assert.Equal([chord], sent.MatchedChords);
        Assert.Equal(matchedScales, sent.MatchedScales);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_NoScalesFound_StillSendsGenerateCandidateTrackInputWithEmptyScales()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        Mock<IRetrieveStep> retrieveStepMock = new();
        retrieveStepMock.Setup(s => s.RetrieveMatchingScales(job.KeyRoot, It.IsAny<IReadOnlyList<MatchedChord>>(), It.IsAny<CancellationToken>()))
            .Returns([]);
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock).HandleAsync(
            new RetrieveScalesInput([chord], WorkflowTestFixtures.CreateMatchedStyle(), job), context, CancellationToken.None);

        GenerateCandidateTrackInput sent = Assert.IsType<GenerateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Empty(sent.MatchedScales);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_RetrieveStepThrows_YieldsGenerationFailedAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        Mock<IRetrieveStep> retrieveStepMock = new();
        retrieveStepMock.Setup(s => s.RetrieveMatchingScales(job.KeyRoot, It.IsAny<IReadOnlyList<MatchedChord>>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("scale lookup failed"));
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock).HandleAsync(
            new RetrieveScalesInput([chord], WorkflowTestFixtures.CreateMatchedStyle(), job), context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal("scale lookup failed", failed.Reason);
        Assert.Empty(context.SentMessages);
    }
}
