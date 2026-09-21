using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Retrieval;
using BackingTrack.Generation.Workflows.Executors;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackingTrack.Generation.Tests.Workflows.Executors;

public class RetrieveStyleExecutorTests
{
    private static RetrieveStyleExecutor CreateExecutor(IRetrieveStep retrieveStepMock) =>
        new(retrieveStepMock, NullLogger<RetrieveStyleExecutor>.Instance);

    [Fact]
    public async Task HandleAsync_StyleMatched_SendsRetrieveChordsInputWithJobAndMatchedStyle()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        IRetrieveStep retrieveStepMock = Substitute.For<IRetrieveStep>();
        retrieveStepMock.RetrieveTopMatchingStyles(job.Style, Arg.Any<CancellationToken>()).Returns(matchedStyle);
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock).HandleAsync(job, context, CancellationToken.None);

        RetrieveChordsInput sent = Assert.IsType<RetrieveChordsInput>(Assert.Single(context.WorkflowMessages));
        Assert.Same(job, sent.Job);
        Assert.Same(matchedStyle, sent.MatchedStyle);
        Assert.Empty(context.YieldedOutputs);

        StatusUpdateInput statusUpdate = Assert.IsType<StatusUpdateInput>(Assert.Single(context.SentMessages.OfType<StatusUpdateInput>()));
        Assert.Equal(job.Id, statusUpdate.JobId);
    }

    [Fact]
    public async Task HandleAsync_NoMatchingStyle_YieldsGenerationFailedAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        IRetrieveStep retrieveStepMock = Substitute.For<IRetrieveStep>();
        retrieveStepMock.RetrieveTopMatchingStyles(job.Style, Arg.Any<CancellationToken>()).Returns((MatchedStyle?)null);
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock).HandleAsync(job, context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Contains(job.Style, failed.Reason);
        Assert.Empty(context.WorkflowMessages);
    }

    [Fact]
    public async Task HandleAsync_RetrieveStepThrows_YieldsGenerationFailedWithExceptionMessage()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        IRetrieveStep retrieveStepMock = Substitute.For<IRetrieveStep>();
        retrieveStepMock.RetrieveTopMatchingStyles(job.Style, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("style lookup failed"));
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock).HandleAsync(job, context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal("style lookup failed", failed.Reason);
        Assert.Empty(context.WorkflowMessages);
    }

    [Fact]
    public async Task HandleAsync_CancellationRequested_YieldsGenerationCancelledAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        IRetrieveStep retrieveStepMock = Substitute.For<IRetrieveStep>();
        retrieveStepMock.RetrieveTopMatchingStyles(job.Style, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        FakeWorkflowContext context = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await CreateExecutor(retrieveStepMock).HandleAsync(job, context, cts.Token);

        Assert.IsType<GenerationCancelled>(Assert.Single(context.YieldedOutputs));
        Assert.Empty(context.WorkflowMessages);
    }
}