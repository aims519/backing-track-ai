using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Tests.KnowledgeBase.Embedding;

public class InMemoryVectorStoreTests
{
    [Fact]
    public void NormaliseVector_KnownVector_ProducesExpectedComponents()
    {
        // A 3-4-5 triangle: magnitude is exactly 5, so the normalised result is exact too.
        ReadOnlyMemory<float> result = InMemoryVectorStore.NormaliseVector([3f, 4f]);

        Assert.Equal(0.6f, result.Span[0], precision: 5);
        Assert.Equal(0.8f, result.Span[1], precision: 5);
    }

    [Fact]
    public void NormaliseVector_AlreadyUnitVector_RemainsUnchanged()
    {
        ReadOnlyMemory<float> result = InMemoryVectorStore.NormaliseVector([1f, 0f, 0f]);

        Assert.Equal(1f, result.Span[0], precision: 5);
        Assert.Equal(0f, result.Span[1], precision: 5);
        Assert.Equal(0f, result.Span[2], precision: 5);
    }

    [Fact]
    public void NormaliseVector_ZeroVector_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => InMemoryVectorStore.NormaliseVector([0f, 0f, 0f]));
    }

    [Theory]
    [InlineData(new[] { 3f, 4f })]
    [InlineData(new[] { 1f, 2f, 3f, 4f, 5f })]
    [InlineData(new[] { -2f, 5f, -1f, 8f })]
    [InlineData(new[] { 0.001f, 0.002f, 0.003f })]
    public void NormaliseVector_ResultHasUnitMagnitude(float[] vector)
    {
       ReadOnlyMemory<float> result = InMemoryVectorStore.NormaliseVector(vector);

        double magnitude = Math.Sqrt(result.ToArray().Sum(x => (double)x * x));

        Assert.Equal(1.0, magnitude, precision: 5);
    }

    [Theory]
    [InlineData(new[] { 3f, 4f })]
    [InlineData(new[] { -2f, 5f, -1f, 8f })]
    public void NormaliseVector_PreservesDirection(float[] vector)
    {
        ReadOnlyMemory<float> result = InMemoryVectorStore.NormaliseVector(vector);

        // Every component should scale down by the same factor, so the ratio
        // between corresponding components is unchanged by normalisation.
        double expectedRatio = (double)vector[0] / result.Span[0];
        for (int i = 1; i < vector.Length; i++)
        {
            double ratio = (double)vector[i] / result.Span[i];
            Assert.Equal(expectedRatio, ratio, precision: 3);
        }
    }

    [Fact]
    public void Constructor_NormalisesVectorsAndPreservesEntries()
    {
        ChordEntry maj = new() { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" };
        ChordEntry min = new() { Id = "min", Intervals = [0, 3, 7], Description = "A minor triad" };

        (IEmbeddable Entry, float[] Vector)[] items =
        [
            (maj, [3f, 4f, 5f]),
            (min, [1f, 2f, 2f])
        ];

        InMemoryVectorStore store = new(items);

        Assert.Equal(2, store.Items.Count);
        Assert.Same(maj, store.Items[0].Entry);
        Assert.Same(min, store.Items[1].Entry);

        foreach ((IEmbeddable _, ReadOnlyMemory<float> vector) in store.Items)
        {
            double magnitude = Math.Sqrt(vector.ToArray().Sum(x => (double)x * x));
            Assert.Equal(1.0, magnitude, precision: 5);
        }
    }
    
    [Fact]
    public void Constructor_NormalisesVectorsThrowsExceptionIfSizesDoNotMatch()
    {
        ChordEntry maj = new() { Id = "maj", Intervals = [0, 4, 7], Description = "A major triad" };
        ChordEntry min = new() { Id = "min", Intervals = [0, 3, 7], Description = "A minor triad" };

        (IEmbeddable Entry, float[] Vector)[] items =
        [
            (maj, [3f, 4f]),
            (min, [1f, 2f, 2f])
        ];

        Assert.Throws<ArgumentException>(() => new InMemoryVectorStore(items));
    }

    [Fact]
    public void DotProduct_KnownVectors_ReturnsExpectedSum()
    {
        InMemoryVectorStore store = new([]);

        ReadOnlyMemory<float> v1 = new float[] { 1f, 2f, 3f };
        ReadOnlyMemory<float> v2 = new float[] { 4f, 5f, 6f };
        
        float result = store.DotProduct(v1, v2);

        Assert.Equal(32f, result, precision: 5);
    }

    [Fact]
    public void DotProduct_OrthogonalVectors_ReturnsZero()
    {
        InMemoryVectorStore store = new([]);

        ReadOnlyMemory<float> v1 = new float[] { 1f, 0f };
        ReadOnlyMemory<float> v2 = new float[] { 0f, 1f };
        
        float result = store.DotProduct(v1, v2);

        Assert.Equal(0f, result, precision: 5);
    }

    [Fact]
    public void DotProduct_MismatchedLengths_ThrowsArgumentException()
    {
        InMemoryVectorStore store = new([]);
        
        ReadOnlyMemory<float> v1= new float[] { 1f, 2f };
        ReadOnlyMemory<float> v2= new float[] { 1f, 2f, 3f };

        Assert.Throws<ArgumentException>(() => store.DotProduct(v1, v2));
    }

    [Fact]
    public void Search_RanksResultsByDescendingSimilarityToQuery()
    {
        ChordEntry chordA = new() { Id = "chordA", Intervals = [0, 4, 7], Description = "A" };
        ChordEntry chordB = new() { Id = "chordB", Intervals = [0, 4, 7], Description = "B" };
        ChordEntry chordC = new() { Id = "chordC", Intervals = [0, 4, 7], Description = "C" };

        InMemoryVectorStore store = new(
        [
            (chordA, [1f, 0f]),      // parallel to query - most similar
            (chordB, [0.9f, 0.1f]),  // close to query direction
            (chordC, [0f, 1f])       // orthogonal to query - least similar
        ]);

        List<(ChordEntry Entry, float Score)> results = store.Search<ChordEntry>([1f, 0f], topK: 3).ToList();

        Assert.Equal(3, results.Count);
        Assert.Equal(["chordA", "chordB", "chordC"], results.Select(r => r.Entry.Id));
        Assert.True(results[0].Score > results[1].Score);
        Assert.True(results[1].Score > results[2].Score);
    }

    [Fact]
    public void Search_LimitsResultsToTopK()
    {
        ChordEntry chordA = new() { Id = "chordA", Intervals = [0, 4, 7], Description = "A" };
        ChordEntry chordB = new() { Id = "chordB", Intervals = [0, 4, 7], Description = "B" };
        ChordEntry chordC = new() { Id = "chordC", Intervals = [0, 4, 7], Description = "C" };

        InMemoryVectorStore store = new(
        [
            (chordA, [1f, 0f]),
            (chordB, [0.9f, 0.1f]),
            (chordC, [0f, 1f])
        ]);

        List<(ChordEntry Entry, float Score)> results = store.Search<ChordEntry>([1f, 0f], topK: 1).ToList();

        Assert.Single(results);
        Assert.Equal("chordA", results[0].Entry.Id);
    }

    [Fact]
    public void Search_OnlyReturnsEntriesMatchingRequestedType()
    {
        ChordEntry chord = new() { Id = "chord", Intervals = [0, 4, 7], Description = "A chord" };
        StyleEntry style = new() { Id = "style", Description = "A style" };

        InMemoryVectorStore store = new(
        [
            (chord, [1f, 0f]),
            (style, [1f, 0f])
        ]);

        List<(ChordEntry Entry, float Score)> chordResults = store.Search<ChordEntry>([1f, 0f], topK: 10).ToList();
        List<(StyleEntry Entry, float Score)> styleResults = store.Search<StyleEntry>([1f, 0f], topK: 10).ToList();

        Assert.Single(chordResults);
        Assert.Equal("chord", chordResults[0].Entry.Id);
        Assert.Single(styleResults);
        Assert.Equal("style", styleResults[0].Entry.Id);
    }

    [Fact]
    public void Search_NoEntriesOfRequestedType_ReturnsEmpty()
    {
        StyleEntry style = new() { Id = "style", Description = "A style" };

        InMemoryVectorStore store = new([(style, [1f, 0f])]);

        List<(ChordEntry Entry, float Score)> results = store.Search<ChordEntry>([1f, 0f], topK: 10).ToList();

        Assert.Empty(results);
    }

    [Fact]
    public void Search_TopKExceedsAvailableMatches_ReturnsAllMatches()
    {
        ChordEntry chord = new() { Id = "chord", Intervals = [0, 4, 7], Description = "A chord" };

        InMemoryVectorStore store = new([(chord, [1f, 0f])]);

        List<(ChordEntry Entry, float Score)> results = store.Search<ChordEntry>([1f, 0f], topK: 10).ToList();

        Assert.Single(results);
    }

    [Fact]
    public void Search_NormalisesUnnormalisedQueryVector()
    {
        ChordEntry chord = new() { Id = "chord", Intervals = [0, 4, 7], Description = "A chord" };

        InMemoryVectorStore store = new([(chord, [1f, 0f])]);

        // An unnormalised query vector pointing the same direction should still
        // score 1.0 (perfect match), not be scaled up by its own magnitude.
        List<(ChordEntry Entry, float Score)> results = store.Search<ChordEntry>([5f, 0f], topK: 1).ToList();

        Assert.Equal(1f, results[0].Score, precision: 5);
    }
}
