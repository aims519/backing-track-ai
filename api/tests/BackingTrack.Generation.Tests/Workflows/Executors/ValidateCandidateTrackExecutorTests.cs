using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.FallbackGeneration;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Executors;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackingTrack.Generation.Tests.Workflows.Executors;

public class ValidateCandidateTrackExecutorTests
{
    private static ValidateCandidateTrackExecutor CreateExecutor(IValidateStep validateStepMock, int maxAttempts, IJobStatusEmitter? statusEmitter = null) =>
        new(validateStepMock, Options.Create(new TrackGenerationWorkflowSettings { GenerationMaxAttempts = maxAttempts }),
            statusEmitter ?? Substitute.For<IJobStatusEmitter>(),
            NullLogger<ValidateCandidateTrackExecutor>.Instance);

    private static ValidateCandidateTrackInput CreateInput(CandidateTrack track, GenerationJob job, int attempts, int criticAttempts = 1, FallbackReason? fallbackReason = null) =>
        new(track, job, WorkflowTestFixtures.CreateMatchedStyle(), [], [], attempts, criticAttempts, fallbackReason);

    [Fact]
    public async Task HandleAsync_TrackIsValid_SendsCriticInputCarryingAttemptsAndSendsNothingElse()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Returns(new TrackValidationResult(true, []));
        IJobStatusEmitter statusEmitterMock = Substitute.For<IJobStatusEmitter>();
        FakeWorkflowContext context = new();

        await CreateExecutor(validateStepMock, maxAttempts: 5, statusEmitterMock).HandleAsync(CreateInput(track, job, attempts: 2, criticAttempts: 1), context, CancellationToken.None);

        CriticInput sent = Assert.IsType<CriticInput>(Assert.Single(context.SentMessages));
        Assert.Same(track, sent.CandidateTrack);
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal(2, sent.ValidationAttempts);
        Assert.Equal(1, sent.CriticAttempts);
        Assert.Empty(context.YieldedOutputs);

        statusEmitterMock.Received(1).Emit(job.Id, "Validation", 2, "Validation passed");
    }

    [Fact]
    public async Task HandleAsync_TrackInvalidBelowMaxAttempts_SendsRepairCandidateTrackInputWithSameAttemptsAndSendsNothingElse()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        ValidationFailure failure = new(ValidationFailureType.PartEmpty, null, "no drum notes");
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Returns(new TrackValidationResult(false, [failure]));
        FakeWorkflowContext context = new();

        await CreateExecutor(validateStepMock, maxAttempts: 5).HandleAsync(CreateInput(track, job, attempts: 2), context, CancellationToken.None);

        RepairCandidateTrackInput sent = Assert.IsType<RepairCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(track, sent.CandidateTrack);
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal([failure], sent.ValidationResult.Failures);
        Assert.Equal(2, sent.ValidationAttempts);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_TrackInvalidAtMaxAttempts_SendsFallbackGenerateCandidateTrackInputAndSendsNothingElse()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Returns(new TrackValidationResult(false, [new ValidationFailure(ValidationFailureType.PartEmpty, null, "no drum notes")]));
        FakeWorkflowContext context = new();

        await CreateExecutor(validateStepMock, maxAttempts: 5).HandleAsync(CreateInput(track, job, attempts: 5), context, CancellationToken.None);

        FallbackGenerateCandidateTrackInput sent = Assert.IsType<FallbackGenerateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal(FallbackReason.ValidationExhausted, sent.FallbackReason);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_TrackInvalidAboveMaxAttempts_StillSendsFallbackGenerateCandidateTrackInput()
    {
        // Defends the `>=` check specifically (not `==`): an attempt count that has overshot
        // GenerationMaxAttempts must still trigger the fallback rather than send another repair message.
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Returns(new TrackValidationResult(false, [new ValidationFailure(ValidationFailureType.PartEmpty, null, "no drum notes")]));
        FakeWorkflowContext context = new();

        await CreateExecutor(validateStepMock, maxAttempts: 5).HandleAsync(CreateInput(track, job, attempts: 6), context, CancellationToken.None);

        FallbackGenerateCandidateTrackInput sent = Assert.IsType<FallbackGenerateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Equal(FallbackReason.ValidationExhausted, sent.FallbackReason);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_FallbackTrackIsValid_SendsPersistCandidateTrackInputAndSkipsCritic()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Returns(new TrackValidationResult(true, []));
        FakeWorkflowContext context = new();

        await CreateExecutor(validateStepMock, maxAttempts: 5).HandleAsync(
            CreateInput(track, job, attempts: 1, criticAttempts: 0, FallbackReason.CriticExhausted), context, CancellationToken.None);

        PersistCandidateTrackInput sent = Assert.IsType<PersistCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(track, sent.CandidateTrack);
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal(1, sent.ValidationAttempts);
        Assert.Equal(FallbackReason.CriticExhausted, sent.FallbackReason);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_FallbackTrackIsInvalid_YieldsGenerationFailedWithoutRetryingOrRepairing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Returns(new TrackValidationResult(false, [new ValidationFailure(ValidationFailureType.PartEmpty, null, "no drum notes")]));
        FakeWorkflowContext context = new();

        // Attempts is deliberately below maxAttempts, to prove a broken fallback track fails the
        // job outright rather than falling through into the repair branch below.
        await CreateExecutor(validateStepMock, maxAttempts: 5).HandleAsync(
            CreateInput(track, job, attempts: 1, criticAttempts: 3, FallbackReason.CriticExhausted), context, CancellationToken.None);

        Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task HandleAsync_ValidateStepThrows_YieldsGenerationFailedWithoutAttemptsAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Throws(new InvalidOperationException("validator blew up"));
        FakeWorkflowContext context = new();

        await CreateExecutor(validateStepMock, maxAttempts: 5).HandleAsync(CreateInput(track, job, attempts: 2), context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal("validator blew up", failed.Reason);
        Assert.Null(failed.Attempts);
        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task HandleAsync_CancellationRequested_YieldsGenerationCancelledAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Throws(new OperationCanceledException());
        FakeWorkflowContext context = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await CreateExecutor(validateStepMock, maxAttempts: 5).HandleAsync(CreateInput(track, job, attempts: 2), context, cts.Token);

        Assert.IsType<GenerationCancelled>(Assert.Single(context.YieldedOutputs));
        Assert.Empty(context.SentMessages);
    }
}