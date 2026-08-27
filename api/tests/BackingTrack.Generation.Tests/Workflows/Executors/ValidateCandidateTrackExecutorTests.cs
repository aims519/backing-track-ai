using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
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
    private static ValidateCandidateTrackExecutor CreateExecutor(IValidateStep validateStepMock, int maxAttempts) =>
        new(validateStepMock, Options.Create(new TrackGenerationWorkflowSettings { GenerationMaxAttempts = maxAttempts }),
            NullLogger<ValidateCandidateTrackExecutor>.Instance);

    private static ValidateCandidateTrackInput CreateInput(CandidateTrack track, GenerationJob job, int attempts) =>
        new(track, job, [], [], attempts);

    [Fact]
    public async Task HandleAsync_TrackIsValid_SendsPersistCandidateTrackInputCarryingAttemptsAndSendsNothingElse()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Returns(new TrackValidationResult(true, []));
        FakeWorkflowContext context = new();

        await CreateExecutor(validateStepMock, maxAttempts: 5).HandleAsync(CreateInput(track, job, attempts: 2), context, CancellationToken.None);

        PersistCandidateTrackInput sent = Assert.IsType<PersistCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(track, sent.CandidateTrack);
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal(2, sent.Attempts);
        Assert.Empty(context.YieldedOutputs);
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
        Assert.Equal(2, sent.Attempts);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_TrackInvalidAtMaxAttempts_YieldsGenerationFailedWithAttemptsAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Returns(new TrackValidationResult(false, [new ValidationFailure(ValidationFailureType.PartEmpty, null, "no drum notes")]));
        FakeWorkflowContext context = new();

        await CreateExecutor(validateStepMock, maxAttempts: 5).HandleAsync(CreateInput(track, job, attempts: 5), context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal(5, failed.Attempts);
        Assert.Empty(context.SentMessages);
    }

    [Fact]
    public async Task HandleAsync_TrackInvalidAboveMaxAttempts_StillYieldsGenerationFailed()
    {
        // Defends the `>=` check specifically (not `==`): an attempt count that has overshot
        // GenerationMaxAttempts must still stop the loop rather than send another repair message.
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        IValidateStep validateStepMock = Substitute.For<IValidateStep>();
        validateStepMock.ValidateCandidateTrack(track, job, Arg.Any<IReadOnlyList<MatchedChord>>(), Arg.Any<IReadOnlyList<MatchedScale>>())
            .Returns(new TrackValidationResult(false, [new ValidationFailure(ValidationFailureType.PartEmpty, null, "no drum notes")]));
        FakeWorkflowContext context = new();

        await CreateExecutor(validateStepMock, maxAttempts: 5).HandleAsync(CreateInput(track, job, attempts: 6), context, CancellationToken.None);

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
}