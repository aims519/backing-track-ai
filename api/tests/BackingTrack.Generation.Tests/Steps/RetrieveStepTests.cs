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

    private static readonly ChordEntry MajChordEntry = new() { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" };

    private static RetrieveStep CreateRetrieveStep()
    {
        (IEmbeddable Entry, float[] Vector)[] items =
        [
            (MajChordEntry, [1f, 0f]),
            (new ChordEntry { Id = "min", Intervals = [0, 3, 7], Description = "Minor triad" }, [1f, 0f]),
            (new ScaleEntry { Id = "major", Intervals = [0, 2, 4, 5, 7, 9, 11], Description = "Major scale" }, [1f, 0f]),
            (new ScaleEntry { Id = "minor", Intervals = [0, 2, 3, 5, 7, 8, 10], Description = "Natural minor scale" },
                [1f, 0f]),
            (new ScaleEntry { Id = "mixolydian", Intervals = [0, 2, 4, 5, 7, 9, 10], Description = "Mixolydian mode" },
                [1f, 0f])
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
            "G", [new MatchedChord(MajChordEntry, "C")]);

        List<string> ids = results.Select(s => s.Id).ToList();
        Assert.Contains("major", ids);
        Assert.Contains("mixolydian", ids);
        Assert.DoesNotContain("minor", ids);
    }

    [Fact]
    public void RetrieveMatchingScaleEntries_UnknownChordRoot_ThrowsArgumentException()
    {
        RetrieveStep retrieveStep = CreateRetrieveStep();

        Assert.Throws<ArgumentException>(() =>
            retrieveStep.RetrieveMatchingScaleEntries("C", [new MatchedChord(MajChordEntry, "H")]));
    }
    
    [Fact]
    public void RetrieveMatchingChords_QualityValidatedByParserButMissingFromVectorStore_ThrowsInvalidOperationException()
    {
        (IEmbeddable Entry, float[] Vector)[] parserItems =
        [
            (new ChordEntry { Id = "xyz", Intervals = [0, 4, 7], Description = "Nonstandard test-only quality" }, new float[] { 1f, 0f })
        ];
        InMemoryVectorStore parserStore = new(parserItems);

        (IEmbeddable Entry, float[] Vector)[] retrieveStoreItems = [ (MajChordEntry, new float[] { 1f, 0f }) ];
        InMemoryVectorStore retrieveStore = new(retrieveStoreItems);

        RetrieveStep retrieveStep = new(retrieveStore, new NotUsedQueryEmbeddingService(), new ScaleMatcher(), new ChordSymbolParser(parserStore));

        Assert.Throws<InvalidOperationException>(() =>
            retrieveStep.RetrieveMatchingChords(["Cxyz"]));
    }
}
