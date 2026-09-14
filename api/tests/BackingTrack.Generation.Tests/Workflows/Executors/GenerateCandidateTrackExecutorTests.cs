using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Executors;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackingTrack.Generation.Tests.Workflows.Executors;

public class GenerateCandidateTrackExecutorTests
{
    private static GenerateCandidateTrackExecutor CreateExecutor(IGenerateStep generateStepMock) =>
        new(generateStepMock, NullLogger<GenerateCandidateTrackExecutor>.Instance);

    [Fact]
    public async Task HandleAsync_TrackGenerated_SendsValidateCandidateTrackInputStartingAtAttemptOne()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        MatchedScale scale = WorkflowTestFixtures.CreateMatchedScale(chord);
        CandidateTrack candidateTrack = new(16, [], [], []);
        IGenerateStep generateStepMock = Substitute.For<IGenerateStep>();
        generateStepMock.GenerateCandidateTrackAsync(job.KeyRoot, job.Tempo, job.TimeSignatureBeatCount,
                job.TimeSignatureBeatUnit, matchedStyle, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>(),
                null, null, Arg.Any<CancellationToken>()).Returns(candidateTrack);
        FakeWorkflowContext context = new();

        await CreateExecutor(generateStepMock).HandleAsync(
            new GenerateCandidateTrackInput(job, matchedStyle, [chord], [scale], CriticAttempts: 1), context, CancellationToken.None);

        ValidateCandidateTrackInput sent = Assert.IsType<ValidateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(candidateTrack, sent.CandidateTrack);
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal(1, sent.ValidationAttempts);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_CriticReviewPresent_GeneratesWithCriticReviewCarryingPreviousAttemptAndReview()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        MatchedScale scale = WorkflowTestFixtures.CreateMatchedScale(chord);
        CandidateTrack previousAttempt = new(16, [], [], []);
        CandidateTrack regeneratedTrack = new(16, [new(0, 1, 40, 100)], [], []);
        CriticFinalReview criticReview = new(false, 40d, [new CriticCitation(CriticDimension.VoiceLeading, "parallel fifths", "kb-1")]);
        IGenerateStep generateStepMock = Substitute.For<IGenerateStep>();
        generateStepMock.GenerateCandidateTrackWithCriticReviewAsync(job.KeyRoot, job.Tempo, job.TimeSignatureBeatCount,
                job.TimeSignatureBeatUnit, matchedStyle, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>(),
                previousAttempt, criticReview, Arg.Any<CancellationToken>()).Returns(regeneratedTrack);
        FakeWorkflowContext context = new();

        await CreateExecutor(generateStepMock).HandleAsync(
            new GenerateCandidateTrackInput(job, matchedStyle, [chord], [scale], CriticAttempts: 2,
                CriticReview: criticReview, PreviousAttempt: previousAttempt), context, CancellationToken.None);

        await generateStepMock.Received(1).GenerateCandidateTrackWithCriticReviewAsync(job.KeyRoot, job.Tempo, job.TimeSignatureBeatCount,
            job.TimeSignatureBeatUnit, matchedStyle, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>(),
            previousAttempt, criticReview, Arg.Any<CancellationToken>());
        await generateStepMock.DidNotReceive().GenerateCandidateTrackAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<MatchedStyle>(), Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>(),
            Arg.Any<CandidateTrack?>(), Arg.Any<IReadOnlyList<ValidationFailure>?>(), Arg.Any<CancellationToken>());

        ValidateCandidateTrackInput sent = Assert.IsType<ValidateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(regeneratedTrack, sent.CandidateTrack);
        Assert.Equal(1, sent.ValidationAttempts);
        Assert.Equal(2, sent.CriticAttempts);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_GenerateStepThrows_YieldsGenerationFailedAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        MatchedScale scale = WorkflowTestFixtures.CreateMatchedScale(chord);
        IGenerateStep generateStepMock = Substitute.For<IGenerateStep>();
        generateStepMock.GenerateCandidateTrackAsync(job.KeyRoot, job.Tempo, job.TimeSignatureBeatCount,
                job.TimeSignatureBeatUnit, matchedStyle, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>(),
                null, null, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("generation failed"));
        FakeWorkflowContext context = new();

        await CreateExecutor(generateStepMock).HandleAsync(
            new GenerateCandidateTrackInput(job, matchedStyle, [chord], [scale], CriticAttempts: 1), context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal("generation failed", failed.Reason);
        Assert.Empty(context.SentMessages);
    }
}