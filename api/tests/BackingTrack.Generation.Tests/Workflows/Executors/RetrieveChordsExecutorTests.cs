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

public class RetrieveChordsExecutorTests
{
    private static RetrieveChordsExecutor CreateExecutor(IRetrieveStep retrieveStepMock, IJobStatusEmitter? statusEmitter = null) =>
        new(retrieveStepMock, statusEmitter ?? Substitute.For<IJobStatusEmitter>(), NullLogger<RetrieveChordsExecutor>.Instance);

    [Fact]
    public async Task HandleAsync_ChordsMatched_SendsRetrieveScalesInputPreservingJobAndStyle()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord[] matchedChords = [WorkflowTestFixtures.CreateMatchedChord()];
        IRetrieveStep retrieveStepMock = Substitute.For<IRetrieveStep>();
        retrieveStepMock.RetrieveMatchingChords(job.Chords).Returns(matchedChords);
        IJobStatusEmitter statusEmitterMock = Substitute.For<IJobStatusEmitter>();
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock, statusEmitterMock).HandleAsync(new RetrieveChordsInput(job, matchedStyle), context, CancellationToken.None);

        RetrieveScalesInput sent = Assert.IsType<RetrieveScalesInput>(Assert.Single(context.SentMessages));
        Assert.Same(job, sent.GenerationJob);
        Assert.Same(matchedStyle, sent.MatchedStyle);
        Assert.Same(matchedChords, sent.MatchedChords);
        Assert.Empty(context.YieldedOutputs);

        statusEmitterMock.Received(1).Emit(job.Id, "Retrieval", 1, Arg.Any<string>());
    }

    [Fact]
    public async Task HandleAsync_RetrieveStepThrows_YieldsGenerationFailedAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        IRetrieveStep retrieveStepMock = Substitute.For<IRetrieveStep>();
        retrieveStepMock.RetrieveMatchingChords(job.Chords)
            .Throws(new InvalidOperationException("chord lookup failed"));
        FakeWorkflowContext context = new();

        await CreateExecutor(retrieveStepMock).HandleAsync(
            new RetrieveChordsInput(job, WorkflowTestFixtures.CreateMatchedStyle()), context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal("chord lookup failed", failed.Reason);
        Assert.Empty(context.SentMessages);
    }
}