using BackingTrack.Generation.Eval.Running;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.TrackGeneration;
using BackingTrack.Generation.Steps.TrackRepair;

namespace BackingTrack.Generation.Eval.Tests.Running;

public class LlmUsageRecorderTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(1);

    [Fact]
    public void Snapshot_AfterGenerateCalls_SumsTokensAndCallsByModelAndTier()
    {
        using LlmUsageRecorder recorder = new();
        using TrackGenerationMetrics metrics = new();

        metrics.RecordSuccess("openai", "recorder-model-a", "flex", Duration, inputTokens: 100, outputTokens: 40);
        metrics.RecordSuccess("openai", "recorder-model-a", "flex", Duration, inputTokens: 50, outputTokens: 10);

        LlmUsage usage = Assert.Single(recorder.Snapshot());
        Assert.Equal(new LlmUsage("generate", "recorder-model-a", "flex", Calls: 2, InputTokens: 150, OutputTokens: 50), usage);
    }

    [Fact]
    public void Snapshot_AcrossStepsAndModels_KeepsThemSeparate()
    {
        using LlmUsageRecorder recorder = new();
        using TrackGenerationMetrics generation = new();
        using TrackRepairMetrics repair = new();
        using CriticMetrics critic = new();

        generation.RecordSuccess("openai", "recorder-big", "flex", Duration, 1_000, 500);
        repair.RecordSuccess("openai", "recorder-small", "flex", Duration, 200, 100);
        critic.RecordSuccess("openai", "recorder-small", "flex", Duration, 300, 30);

        IReadOnlyList<LlmUsage> snapshot = recorder.Snapshot();

        Assert.Equal(3, snapshot.Count);
        Assert.Contains(new LlmUsage("generate", "recorder-big", "flex", 1, 1_000, 500), snapshot);
        Assert.Contains(new LlmUsage("repair", "recorder-small", "flex", 1, 200, 100), snapshot);
        Assert.Contains(new LlmUsage("critic", "recorder-small", "flex", 1, 300, 30), snapshot);
    }

    [Fact]
    public void Snapshot_CallThatFailedBeforeReportingTokens_StillCountsTheCall()
    {
        using LlmUsageRecorder recorder = new();
        using TrackGenerationMetrics metrics = new();

        metrics.RecordFailure("openai", "recorder-model-b", "flex", Duration);

        LlmUsage usage = Assert.Single(recorder.Snapshot());
        Assert.Equal(1, usage.Calls);
        Assert.Equal(0, usage.InputTokens);
    }

    [Fact]
    public void Reset_DiscardsWhatWasRecordedSoFar()
    {
        using LlmUsageRecorder recorder = new();
        using TrackGenerationMetrics metrics = new();
        metrics.RecordSuccess("openai", "recorder-model-c", "flex", Duration, 100, 40);

        recorder.Reset();
        metrics.RecordSuccess("openai", "recorder-model-c", "flex", Duration, 7, 3);

        LlmUsage usage = Assert.Single(recorder.Snapshot());
        Assert.Equal(7, usage.InputTokens);
        Assert.Equal(3, usage.OutputTokens);
    }

    [Fact]
    public void Snapshot_NothingRecorded_IsEmpty()
    {
        using LlmUsageRecorder recorder = new();

        Assert.Empty(recorder.Snapshot());
    }
}