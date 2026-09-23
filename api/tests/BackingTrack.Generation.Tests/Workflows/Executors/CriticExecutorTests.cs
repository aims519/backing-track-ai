using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Executors;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackingTrack.Generation.Tests.Workflows.Executors;

public class CriticExecutorTests
{
    private static CriticExecutor CreateExecutor(ICriticStep criticStepMock, int maxAttempts, IJobStatusEmitter? statusEmitter = null) =>
        new(criticStepMock, Options.Create(new CriticSettings { PassingScoreThreshold = 66d, MaxAttempts = maxAttempts }),
            statusEmitter ?? Substitute.For<IJobStatusEmitter>(),
            NullLogger<CriticExecutor>.Instance);

    private static CriticInput CreateInput(CandidateTrack track, GenerationJob job, int validationAttempts, int criticAttempts) =>
        new(track, job, WorkflowTestFixtures.CreateMatchedStyle(), [], [], validationAttempts, criticAttempts);

    [Fact]
    public async Task HandleAsync_ReviewPasses_SendsPersistCandidateTrackInputCarryingBothAttemptsAndSendsNothingElse()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        ICriticStep criticStepMock = Substitute.For<ICriticStep>();
        criticStepMock.ReviewCandidateTrackAsync(track, job, Arg.Any<MatchedStyle>(), Arg.Any<IReadOnlyList<MatchedChord>>(),
                Arg.Any<IReadOnlyList<MatchedScale>>(), Arg.Any<CancellationToken>())
            .Returns(new CriticFinalReview(true, 100d, []));
        IJobStatusEmitter statusEmitterMock = Substitute.For<IJobStatusEmitter>();
        FakeWorkflowContext context = new();

        await CreateExecutor(criticStepMock, maxAttempts: 3, statusEmitterMock).HandleAsync(CreateInput(track, job, validationAttempts: 2, criticAttempts: 1), context, CancellationToken.None);

        PersistCandidateTrackInput sent = Assert.IsType<PersistCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(track, sent.CandidateTrack);
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal(2, sent.ValidationAttempts);
        Assert.Equal(1, sent.CriticAttempts);
        Assert.Empty(context.YieldedOutputs);

        statusEmitterMock.Received(1).Emit(job.Id, "Critic Review", 1, "Critic review passed with score 100%");
    }

    [Fact]
    public async Task HandleAsync_ReviewFailsBelowMaxAttempts_SendsGenerateCandidateTrackInputWithReviewAndPreviousAttempt()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        CriticFinalReview review = new(false, 33d, [new CriticCitation(CriticDimension.VoiceLeading, "parallel fifths", "kb-1")]);
        ICriticStep criticStepMock = Substitute.For<ICriticStep>();
        criticStepMock.ReviewCandidateTrackAsync(track, job, Arg.Any<MatchedStyle>(), Arg.Any<IReadOnlyList<MatchedChord>>(),
                Arg.Any<IReadOnlyList<MatchedScale>>(), Arg.Any<CancellationToken>())
            .Returns(review);
        FakeWorkflowContext context = new();

        await CreateExecutor(criticStepMock, maxAttempts: 3).HandleAsync(CreateInput(track, job, validationAttempts: 2, criticAttempts: 1), context, CancellationToken.None);

        GenerateCandidateTrackInput sent = Assert.IsType<GenerateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(job, sent.GenerationJob);
        Assert.Same(review, sent.CriticReview);
        Assert.Same(track, sent.PreviousAttempt);
        Assert.Equal(2, sent.CriticAttempts);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_ReviewFailsAtMaxAttempts_SendsFallbackGenerateCandidateTrackInputWithoutSpendingAnotherGenerateRound()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        CriticFinalReview review = new(false, 33d, [new CriticCitation(CriticDimension.VoiceLeading, "parallel fifths", "kb-1")]);
        ICriticStep criticStepMock = Substitute.For<ICriticStep>();
        criticStepMock.ReviewCandidateTrackAsync(track, job, Arg.Any<MatchedStyle>(), Arg.Any<IReadOnlyList<MatchedChord>>(),
                Arg.Any<IReadOnlyList<MatchedScale>>(), Arg.Any<CancellationToken>())
            .Returns(review);
        FakeWorkflowContext context = new();

        await CreateExecutor(criticStepMock, maxAttempts: 3).HandleAsync(CreateInput(track, job, validationAttempts: 2, criticAttempts: 3), context, CancellationToken.None);

        FallbackGenerateCandidateTrackInput sent = Assert.IsType<FallbackGenerateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(job, sent.GenerationJob);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_CriticStepThrows_YieldsGenerationFailedAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        ICriticStep criticStepMock = Substitute.For<ICriticStep>();
        criticStepMock.ReviewCandidateTrackAsync(track, job, Arg.Any<MatchedStyle>(), Arg.Any<IReadOnlyList<MatchedChord>>(),
                Arg.Any<IReadOnlyList<MatchedScale>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("critic blew up"));
        FakeWorkflowContext context = new();

        await CreateExecutor(criticStepMock, maxAttempts: 3).HandleAsync(CreateInput(track, job, validationAttempts: 2, criticAttempts: 1), context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal("critic blew up", failed.Reason);
        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task HandleAsync_CancellationRequested_YieldsGenerationCancelledAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        ICriticStep criticStepMock = Substitute.For<ICriticStep>();
        criticStepMock.ReviewCandidateTrackAsync(track, job, Arg.Any<MatchedStyle>(), Arg.Any<IReadOnlyList<MatchedChord>>(),
                Arg.Any<IReadOnlyList<MatchedScale>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        FakeWorkflowContext context = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await CreateExecutor(criticStepMock, maxAttempts: 3).HandleAsync(CreateInput(track, job, validationAttempts: 2, criticAttempts: 1), context, cts.Token);

        Assert.IsType<GenerationCancelled>(Assert.Single(context.YieldedOutputs));
        Assert.Empty(context.SentMessages);
    }
}