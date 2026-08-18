using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps;

namespace BackingTrack.Generation.Tests.Steps;

public class RetrieveStepTests
{
    // Not exercised by any test here - RetrieveMatchingScaleEntries never calls it.
    private class NotUsedQueryEmbeddingService : IQueryEmbeddingService
    {
        public Task<float[]> EmbedQueryAsync(string queryText, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Not expected to be called by these tests");
    }

    private static RetrieveStep CreateRetrieveStep()
    {
        (IEmbeddable Entry, float[] Vector)[] items =
        [
            (new ChordEntry { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" }, new float[] { 1f, 0f }),
            (new ChordEntry { Id = "min", Intervals = [0, 3, 7], Description = "Minor triad" }, new float[] { 1f, 0f }),
            (new ScaleEntry { Id = "major", Intervals = [0, 2, 4, 5, 7, 9, 11], Description = "Major scale" }, new float[] { 1f, 0f }),
            (new ScaleEntry { Id = "minor", Intervals = [0, 2, 3, 5, 7, 8, 10], Description = "Natural minor scale" }, new float[] { 1f, 0f }),
            (new ScaleEntry { Id = "mixolydian", Intervals = [0, 2, 4, 5, 7, 9, 10], Description = "Mixolydian mode" }, new float[] { 1f, 0f })
        ];

        InMemoryVectorStore store = new(items);
        return new RetrieveStep(store, new NotUsedQueryEmbeddingService(), new ScaleMatcher(), new ChordSymbolParser(store));
    }

    // The music-theory correctness of the matching itself (transposition, multi-chord
    // narrowing, etc.) is covered by ScaleMatcherTests against ScaleMatcher directly. This
    // just confirms RetrieveStep wires chord/scale entries from the vector store into it
    // correctly - one happy-path case is enough for that.
    [Fact]
    public void RetrieveMatchingScaleEntries_ResolvesChordQualitiesFromVectorStoreAndDelegatesToScaleMatcher()
    {
        RetrieveStep retrieveStep = CreateRetrieveStep();

        IEnumerable<ScaleEntry> results = retrieveStep.RetrieveMatchingScaleEntries(
            "G", [("C", "maj")]);

        List<string> ids = results.Select(s => s.Id).ToList();
        Assert.Contains("major", ids);
        Assert.Contains("mixolydian", ids);
        Assert.DoesNotContain("minor", ids);
    }

    [Fact]
    public void RetrieveMatchingScaleEntries_UnknownChordQuality_Throws()
    {
        RetrieveStep retrieveStep = CreateRetrieveStep();

        // No ChordEntry with Id "xyz" exists in the store - RetrieveStep's lookup
        // (allChordEntries.First(...)) throws InvalidOperationException, not the
        // ArgumentException ScaleMatcher.FindMatchingScales throws for a bad root.
        Assert.Throws<InvalidOperationException>(() =>
            retrieveStep.RetrieveMatchingScaleEntries("C", [("C", "xyz")]));
    }

    [Fact]
    public void RetrieveMatchingScaleEntries_UnknownChordRoot_ThrowsArgumentException()
    {
        RetrieveStep retrieveStep = CreateRetrieveStep();

        Assert.Throws<ArgumentException>(() =>
            retrieveStep.RetrieveMatchingScaleEntries("C", [("H", "maj")]));
    }
}
