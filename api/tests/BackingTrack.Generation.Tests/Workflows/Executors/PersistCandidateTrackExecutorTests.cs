using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Workflows.Executors;
using BackingTrack.Generation.Workflows.Inputs;
using BackingTrack.Generation.Workflows.Outputs;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Tests.Workflows.Executors;

public class PersistCandidateTrackExecutorTests
{
    private static PersistCandidateTrackExecutor CreateExecutor(string baseFolderPath) =>
        new(Options.Create(new TrackStorageSettings { BaseFolderPath = baseFolderPath }),
            NullLogger<PersistCandidateTrackExecutor>.Instance);

    [Fact]
    public async Task HandleAsync_WriteSucceeds_YieldsGenerationSucceededAndWritesTrackFile()
    {
        string baseFolderPath = Path.Combine(Path.GetTempPath(), $"backing-track-tests-{Guid.NewGuid()}");
        try
        {
            GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
            CandidateTrack track = new(16, [], [], []);
            FakeWorkflowContext context = new();

            await CreateExecutor(baseFolderPath).HandleAsync(new PersistCandidateTrackInput(track, job, 3), context, CancellationToken.None);

            GenerationSucceeded succeeded = Assert.IsType<GenerationSucceeded>(Assert.Single(context.YieldedOutputs));
            Assert.Same(track, succeeded.CandidateTrack);
            Assert.Equal(3, succeeded.Attempts);
            Assert.Empty(context.SentMessages);

            string expectedFilePath = Path.Combine(baseFolderPath, $"{succeeded.TrackId}.json");
            Assert.True(File.Exists(expectedFilePath));
        }
        finally
        {
            if (Directory.Exists(baseFolderPath))
                Directory.Delete(baseFolderPath, recursive: true);
        }
    }

    [Fact]
    public async Task HandleAsync_WriteFails_YieldsGenerationFailedWithAttemptsAndSendsNothing()
    {
        // A regular file sitting at the target path makes Directory.CreateDirectory throw,
        // giving a deterministic write failure without needing to fake the filesystem.
        string blockingFilePath = Path.Combine(Path.GetTempPath(), $"backing-track-tests-{Guid.NewGuid()}");
        await File.WriteAllTextAsync(blockingFilePath, "not a directory", TestContext.Current.CancellationToken);
        try
        {
            GenerationJob job = WorkflowTestFixtures.CreateGenerationJob();
            CandidateTrack track = new(16, [], [], []);
            FakeWorkflowContext context = new();

            await CreateExecutor(blockingFilePath).HandleAsync(new PersistCandidateTrackInput(track, job, 4), context, CancellationToken.None);

            GenerationFailed failed = Assert.IsType<GenerationFailed>(Assert.Single(context.YieldedOutputs));
            Assert.Equal(4, failed.Attempts);
            Assert.Empty(context.SentMessages);
        }
        finally
        {
            File.Delete(blockingFilePath);
        }
    }
}