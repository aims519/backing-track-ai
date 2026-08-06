using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Tests.KnowledgeBase.Embedding;

public class InMemoryVectorStoreTests
{
    [Fact]
    public void NormaliseVector_KnownVector_ProducesExpectedComponents()
    {
        // A 3-4-5 triangle: magnitude is exactly 5, so the normalised result is exact too.
        float[] result = InMemoryVectorStore.NormaliseVector([3f, 4f]);

        Assert.Equal(0.6f, result[0], precision: 5);
        Assert.Equal(0.8f, result[1], precision: 5);
    }

    [Fact]
    public void NormaliseVector_AlreadyUnitVector_RemainsUnchanged()
    {
        float[] result = InMemoryVectorStore.NormaliseVector([1f, 0f, 0f]);

        Assert.Equal(1f, result[0], precision: 5);
        Assert.Equal(0f, result[1], precision: 5);
        Assert.Equal(0f, result[2], precision: 5);
    }

    [Theory]
    [InlineData(new[] { 3f, 4f })]
    [InlineData(new[] { 1f, 2f, 3f, 4f, 5f })]
    [InlineData(new[] { -2f, 5f, -1f, 8f })]
    [InlineData(new[] { 0.001f, 0.002f, 0.003f })]
    public void NormaliseVector_ResultHasUnitMagnitude(float[] vector)
    {
        float[] result = InMemoryVectorStore.NormaliseVector(vector);

        double magnitude = Math.Sqrt(result.Sum(x => (double)x * x));

        Assert.Equal(1.0, magnitude, precision: 5);
    }

    [Theory]
    [InlineData(new[] { 3f, 4f })]
    [InlineData(new[] { -2f, 5f, -1f, 8f })]
    public void NormaliseVector_PreservesDirection(float[] vector)
    {
        float[] result = InMemoryVectorStore.NormaliseVector(vector);

        // Every component should scale down by the same factor, so the ratio
        // between corresponding components is unchanged by normalisation.
        double expectedRatio = (double)vector[0] / result[0];
        for (int i = 1; i < vector.Length; i++)
        {
            double ratio = (double)vector[i] / result[i];
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
            (maj, [3f, 4f]),
            (min, [1f, 2f, 2f])
        ];

        InMemoryVectorStore store = new(items);

        Assert.Equal(2, store.Items.Length);
        Assert.Same(maj, store.Items[0].Entry);
        Assert.Same(min, store.Items[1].Entry);

        foreach ((IEmbeddable _, float[] vector) in store.Items)
        {
            double magnitude = Math.Sqrt(vector.Sum(x => (double)x * x));
            Assert.Equal(1.0, magnitude, precision: 5);
        }
    }
}
