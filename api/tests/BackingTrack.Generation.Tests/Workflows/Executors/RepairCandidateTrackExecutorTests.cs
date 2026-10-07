using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Types;
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
    private static RepairCandidateTrackExecutor CreateExecutor(IRepairStep repairStepMock, IJobStatusEmitter? statusEmitter = null) =>
        new(repairStepMock, statusEmitter ?? Substitute.For<IJobStatusEmitter>(), NullLogger<RepairCandidateTrackExecutor>.Instance);

    private static RepairCandidateTrackInput CreateInput(CandidateTrack track, GenerationJob job, IReadOnlyList<ValidationFailure> failures, int attempts, int criticAttempts = 1) =>
        new(track, new TrackValidationResult(false, failures), job, WorkflowTestFixtures.CreateMatchedStyle(), [], [], attempts, criticAttempts);

    [Fact]
    public async Task HandleAsync_RepairSucceeds_SendsValidateCandidateTrackInputWithIncrementedAttempts()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack originalTrack = new(16, [], [], []);
        CandidateTrack repairedTrack = new(16, [new(0, 1, 40, 100)], [], []);
        ValidationFailure failure = new(ValidationFailureType.PartEmpty, null, "no drum notes");
        IRepairStep repairStepMock = Substitute.For<IRepairStep>();
        repairStepMock.RepairCandidateTrackAsync(originalTrack, Arg.Is<IReadOnlyList<ValidationFailure>>(f => f.Contains(failure)), Arg.Any<CancellationToken>()).Returns(repairedTrack);
        IJobStatusEmitter statusEmitterMock = Substitute.For<IJobStatusEmitter>();
        FakeWorkflowContext context = new();

        await CreateExecutor(repairStepMock, statusEmitterMock).HandleAsync(CreateInput(originalTrack, job, [failure], attempts: 2), context, CancellationToken.None);

        ValidateCandidateTrackInput sent = Assert.IsType<ValidateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(repairedTrack, sent.CandidateTrack);
        Assert.Same(job, sent.GenerationJob);
        Assert.Equal(3, sent.ValidationAttempts);
        Assert.Empty(context.YieldedOutputs);

        statusEmitterMock.Received(1).Emit(job.Id, "Repair", 2, Arg.Any<string>());
    }

    [Fact]
    public async Task HandleAsync_RepairStepThrows_SendsFallbackInputCarryingMatchedDataAndYieldsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        ValidationFailure failure = new(ValidationFailureType.PartEmpty, null, "no drum notes");
        MatchedStyle matchedStyle = WorkflowTestFixtures.CreateMatchedStyle();
        MatchedChord chord = WorkflowTestFixtures.CreateMatchedChord();
        MatchedScale scale = WorkflowTestFixtures.CreateMatchedScale(chord);
        IRepairStep repairStepMock = Substitute.For<IRepairStep>();
        repairStepMock.RepairCandidateTrackAsync(track, Arg.Any<IReadOnlyList<ValidationFailure>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("repair failed"));
        FakeWorkflowContext context = new();
        RepairCandidateTrackInput input = new(track, new TrackValidationResult(false, [failure]), job, matchedStyle, [chord], [scale], 2, 1);

        await CreateExecutor(repairStepMock).HandleAsync(input, context, CancellationToken.None);

        FallbackGenerateCandidateTrackInput sent = Assert.IsType<FallbackGenerateCandidateTrackInput>(Assert.Single(context.SentMessages));
        Assert.Same(job, sent.GenerationJob);
        Assert.Same(matchedStyle, sent.MatchedStyle);
        Assert.Same(chord, Assert.Single(sent.MatchedChords));
        Assert.Same(scale, Assert.Single(sent.MatchedScales));
        Assert.Empty(context.YieldedOutputs);
    }

    [Fact]
    public async Task HandleAsync_CancellationRequested_YieldsGenerationCancelledAndSendsNothing()
    {
        GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
        CandidateTrack track = new(16, [], [], []);
        ValidationFailure failure = new(ValidationFailureType.PartEmpty, null, "no drum notes");
        IRepairStep repairStepMock = Substitute.For<IRepairStep>();
        repairStepMock.RepairCandidateTrackAsync(track, Arg.Any<IReadOnlyList<ValidationFailure>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        FakeWorkflowContext context = new();
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await CreateExecutor(repairStepMock).HandleAsync(CreateInput(track, job, [failure], attempts: 2), context, cts.Token);

        Assert.IsType<GenerationCancelled>(Assert.Single(context.YieldedOutputs));
        Assert.Empty(context.SentMessages);
    }
}