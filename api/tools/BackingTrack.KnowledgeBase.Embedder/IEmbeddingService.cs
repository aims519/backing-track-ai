using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.KnowledgeBase.Embedder;

public interface IEmbeddingService
{
    (IEnumerable<ChordEntry> chordEntries, IEnumerable<ScaleEntry> scaleEntries, IEnumerable<StyleEntry> styleEntries) ParseRagFiles();
    IAsyncEnumerable<(IEmbeddable Embeddable, float[] EmbedVector)> EmbedBatchAsync(IReadOnlyList<IEmbeddable> embeddables);
}