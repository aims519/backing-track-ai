using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Retrieval;
using BackingTrack.Generation.Workflows.Executors;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace BackingTrack.Generation.Tests.Workflows.Executors;

public class RetrieveStyleExecutorTests
{
    private static RetrieveStyleExecutor CreateExecutor(Mock<IRetrieveStep> retrieveStepMock) =>
        new(retrieveStepMock.Object, NullLogger<RetrieveStyleExecutor>.Instance);

    [Fact]
    public async Task HandleAsync_StyleMatched_SendsRetrieveChordsInputWithJobAndMatchedStyle()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        Mock<IRetrieveStep> retrieveStepMock = new();
        retrieveStepMock.Setup(s => s.RetrieveTopMatchingStyles(job.Style, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matchedStyle);
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock).HandleAsync(job, context, CancellationToken.None);

        RetrieveChordsInput sent = Assert.IsType<RetrieveChordsInput>(Assert.Single(context.SentMessages));
        Assert.Same(job, sent.Job);
        Assert.Same(matchedStyle, sent.MatchedStyle);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_NoMatchingStyle_YieldsGenerationFailedAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        Mock<IRetrieveStep> retrieveStepMock = new();
        retrieveStepMock.Setup(s => s.RetrieveTopMatchingStyles(job.Style, It.IsAny<CancellationToken>()))
            .ReturnsAsync((MatchedStyle?)null);
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock).HandleAsync(job, context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Contains(job.Style, failed.Reason);
        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task HandleAsync_RetrieveStepThrows_YieldsGenerationFailedWithExceptionMessage()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        Mock<IRetrieveStep> retrieveStepMock = new();
        retrieveStepMock.Setup(s => s.RetrieveTopMatchingStyles(job.Style, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("style lookup failed"));
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock).HandleAsync(job, context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal("style lookup failed", failed.Reason);
        Assert.Empty(context.SentMessages);
    }
}
