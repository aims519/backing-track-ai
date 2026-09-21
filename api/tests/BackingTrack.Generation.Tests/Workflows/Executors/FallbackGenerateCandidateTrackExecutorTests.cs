using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Executors;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackingTrack.Generation.Tests.Workflows.Executors;

public class FallbackGenerateCandidateTrackExecutorTests
{
    private static FallbackGenerateCandidateTrackExecutor CreateExecutor(IFallbackGenerationStep fallbackGenerationStepMock) =>
        new(fallbackGenerationStepMock, NullLogger<FallbackGenerateCandidateTrackExecutor>.Instance);

    [Fact]
    public async Task HandleAsync_FallbackGenerationSucceeds_SendsValidateCandidateTrackInputMarkedAsFallback()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        MatchedScale scale = WorkflowTestFixtures.CreateMatchedScale(chord);
        CandidateTrack fallbackTrack = new(16, [], [], []);
        IFallbackGenerationStep fallbackGenerationStepMock = Substitute.For<IFallbackGenerationStep>();
        fallbackGenerationStepMock.GenerateFallbackCandidateTrackAsync(job.TimeSignatureBeatCount, job.TimeSignatureBeatUnit,
                matchedStyle, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<CancellationToken>())
            .Returns(fallbackTrack);
        FakeWorkflowContext context = new();

        await CreateExecutor(fallbackGenerationStepMock).HandleAsync(
            new FallbackGenerateCandidateTrackInput(job, matchedStyle, [chord], [scale]), context, CancellationToken.None);

        ValidateCandidateTrackInput sent = Assert.IsType<ValidateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(fallbackTrack, sent.CandidateTrack);
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal(1, sent.ValidationAttempts);
        Assert.Equal(0, sent.CriticAttempts);
        Assert.True(sent.IsFallbackTrack);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_FallbackGenerationStepThrows_YieldsGenerationFailedAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        MatchedScale scale = WorkflowTestFixtures.CreateMatchedScale(chord);
        IFallbackGenerationStep fallbackGenerationStepMock = Substitute.For<IFallbackGenerationStep>();
        fallbackGenerationStepMock.GenerateFallbackCandidateTrackAsync(job.TimeSignatureBeatCount, job.TimeSignatureBeatUnit,
                matchedStyle, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("fallback generation failed"));
        FakeWorkflowContext context = new();

        await CreateExecutor(fallbackGenerationStepMock).HandleAsync(
            new FallbackGenerateCandidateTrackInput(job, matchedStyle, [chord], [scale]), context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal("fallback generation failed", failed.Reason);
        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task HandleAsync_CancellationRequested_YieldsGenerationCancelledAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        MatchedScale scale = WorkflowTestFixtures.CreateMatchedScale(chord);
        IFallbackGenerationStep fallbackGenerationStepMock = Substitute.For<IFallbackGenerationStep>();
        fallbackGenerationStepMock.GenerateFallbackCandidateTrackAsync(job.TimeSignatureBeatCount, job.TimeSignatureBeatUnit,
                matchedStyle, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        FakeWorkflowContext context = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await CreateExecutor(fallbackGenerationStepMock).HandleAsync(
            new FallbackGenerateCandidateTrackInput(job, matchedStyle, [chord], [scale]), context, cts.Token);

        Assert.IsType<GenerationCancelled>(Assert.Single(context.YieldedOutputs));
        Assert.Empty(context.SentMessages);
    }
}