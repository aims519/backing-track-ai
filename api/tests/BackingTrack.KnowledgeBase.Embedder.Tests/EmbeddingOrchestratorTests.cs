using BackingTrack.Generation.KnowledgeBase.Types;

using Microsoft.Extensions.Logging.Abstractions;

namespace BackingTrack.KnowledgeBase.Embedder.Tests;

public class EmbeddingOrchestratorTests
{
    [Fact]
    public void ValidateForDuplicateIds_AllIdsUnique_DoesNotThrow()
    {
        FakeEmbeddingService embeddingService = new(
            chordEntries: [new ChordEntry { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" }],
            scaleEntries: [new ScaleEntry { Id = "major_pentatonic", Intervals = [0, 2, 4, 7, 9], Description = "Major pentatonic scale" }],
            styleEntries: [new StyleEntry { Id = "blues", Description = "Blues style" }]);

        EmbeddingOrchestrator orchestrator = new(NullLogger<EmbeddingOrchestrator>.Instance, embeddingService);

        Exception? exception = Record.Exception(orchestrator.ValidateForDuplicateIds);

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateForDuplicateIds_DuplicateWithinSameEntryType_Throws()
    {
        FakeEmbeddingService embeddingService = new(
            chordEntries:
            [
                new ChordEntry { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" },
                new ChordEntry { Id = "maj", Intervals = [0, 4, 7], Description = "Duplicate major triad" }
            ],
            scaleEntries: [],
            styleEntries: []);

        EmbeddingOrchestrator orchestrator = new(NullLogger<EmbeddingOrchestrator>.Instance, embeddingService);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(orchestrator.ValidateForDuplicateIds);

        Assert.Contains("maj", exception.Message);
    }

    [Fact]
    public void ValidateForDuplicateIds_DuplicateAcrossDifferentEntryTypes_Throws()
    {
        FakeEmbeddingService embeddingService = new(
            chordEntries: [new ChordEntry { Id = "shared_id", Intervals = [0, 4, 7], Description = "A major triad" }],
            scaleEntries: [new ScaleEntry { Id = "shared_id", Intervals = [0, 2, 4, 7, 9], Description = "Major pentatonic scale" }],
            styleEntries: []);

        EmbeddingOrchestrator orchestrator = new(NullLogger<EmbeddingOrchestrator>.Instance, embeddingService);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(orchestrator.ValidateForDuplicateIds);

        Assert.Contains("shared_id", exception.Message);
    }

    [Fact]
    public void ValidateForDuplicateIds_NoEntries_DoesNotThrow()
    {
        FakeEmbeddingService embeddingService = new(chordEntries: [], scaleEntries: [], styleEntries: []);

        EmbeddingOrchestrator orchestrator = new(NullLogger<EmbeddingOrchestrator>.Instance, embeddingService);

        Exception? exception = Record.Exception(orchestrator.ValidateForDuplicateIds);

        Assert.Null(exception);
    }

    private sealed class FakeEmbeddingService(
        IEnumerable<ChordEntry> chordEntries,
        IEnumerable<ScaleEntry> scaleEntries,
        IEnumerable<StyleEntry> styleEntries) : IEmbeddingService
    {
        public (IEnumerable<ChordEntry> chordEntries, IEnumerable<ScaleEntry> scaleEntries, IEnumerable<StyleEntry> styleEntries) ParseRagFiles() =>
            (chordEntries, scaleEntries, styleEntries);

        public IAsyncEnumerable<(IEmbeddable Embeddable, float[] EmbedVector)> EmbedBatchAsync(IReadOnlyList<IEmbeddable> embeddables) =>
            throw new NotImplementedException("Not exercised by these tests.");
    }
}