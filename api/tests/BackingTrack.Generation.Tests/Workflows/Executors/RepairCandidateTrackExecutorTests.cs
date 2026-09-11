using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Steps.TrackRepair;
using BackingTrack.Generation.Steps.TrackValidation;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Executors;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackingTrack.Generation.Tests.Workflows.Executors;

public class RepairCandidateTrackExecutorTests
{
    private static RepairCandidateTrackExecutor CreateExecutor(IRepairStep repairStepMock) =>
        new(repairStepMock, NullLogger<RepairCandidateTrackExecutor>.Instance);

    private static RepairCandidateTrackInput CreateInput(CandidateTrack track, GenerationJob job, IReadOnlyList<ValidationFailure> failures, int attempts) =>
        new(track, new TrackValidationResult(false, failures), job, WorkflowTestFixtures.CreateMatchedStyle(), [], [], attempts);

    [Fact]
    public async Task HandleAsync_RepairSucceeds_SendsValidateCandidateTrackInputWithIncrementedAttempts()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack originalTrack = new(16, [], [], []);
        CandidateTrack repairedTrack = new(16, [new(0, 1, 40, 100)], [], []);
        ValidationFailure failure = new(ValidationFailureType.PartEmpty, null, "no drum notes");
        IRepairStep repairStepMock = Substitute.For<IRepairStep>();
        repairStepMock.RepairCandidateTrackAsync(originalTrack, Arg.Is<IReadOnlyList<ValidationFailure>>(f => f.Contains(failure)), Arg.Any<CancellationToken>()).Returns(repairedTrack);
        FakeWorkflowContext context = new();

        await CreateExecutor(repairStepMock).HandleAsync(CreateInput(originalTrack, job, [failure], attempts: 2), context, CancellationToken.None);

        ValidateCandidateTrackInput sent = Assert.IsType<ValidateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(repairedTrack, sent.CandidateTrack);
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal(3, sent.Attempts);
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_RepairStepThrows_YieldsGenerationFailedWithAttemptsAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        ValidationFailure failure = new(ValidationFailureType.PartEmpty, null, "no drum notes");
        IRepairStep repairStepMock = Substitute.For<IRepairStep>();
        repairStepMock.RepairCandidateTrackAsync(track, Arg.Any<IReadOnlyList<ValidationFailure>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("repair failed"));
        FakeWorkflowContext context = new();

        await CreateExecutor(repairStepMock).HandleAsync(CreateInput(track, job, [failure], attempts: 2), context, CancellationToken.None);

        GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
        Assert.Equal("repair failed", failed.Reason);
        Assert.Equal(2, failed.Attempts);
        Assert.Empty(context.SentMessages);
    }
}