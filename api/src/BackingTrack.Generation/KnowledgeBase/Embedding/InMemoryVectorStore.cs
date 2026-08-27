using System.Numerics.Tensors;

using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.KnowledgeBase.Embedding;

/// <summary>
/// Brute-force, in-memory dot-product search over L2-normalised embeddings for the RAG
/// knowledge base. See ADR 0015 (docs/adr/0015-in-memory-vector-retrieval-for-rag-kb.md)
/// for why this is a self-written scan rather than a vector database
/// </summary>
public class InMemoryVectorStore(IEnumerable<(IEmbeddable Entry, float[] Vector)> items)
{
    public readonly IReadOnlyList<(IEmbeddable Entry, ReadOnlyMemory<float> Vector)> Items = Normalise(items);

    private static (IEmbeddable Entry, ReadOnlyMemory<float> Vector)[] Normalise(IEnumerable<(IEmbeddable Entry, float[] Vector)> items)
    {
        IReadOnlyList<(IEmbeddable Entry, float[] Vector)> itemList = items.ToList();

        if (itemList.Count > 1)
        {
            int expectedVectorLength = itemList.First().Vector.Length;
            if (itemList.Select(i => i.Vector).Any(i => i.Length != expectedVectorLength))
                throw new ArgumentException("All vectors must have the same length", nameof(items));
        }

        return itemList.Select(item => (item.Entry, NormaliseVector(item.Vector))).ToArray();
    }

    internal static ReadOnlyMemory<float> NormaliseVector(float[] vector)
    {
        float magnitude = VectorMagnitude(vector);
        if (magnitude == 0f)
            throw new ArgumentException("Cannot normalise a zero vector", nameof(vector));

        float[] normalisedVector = new float[vector.Length];

        for (int i = 0; i < vector.Length; i++)
        {
            normalisedVector[i] = vector[i] / magnitude;
        }

        return normalisedVector;
    }

    private static float VectorMagnitude(float[] vector) => (float)Math.Sqrt(vector.Sum(x => x * x));

    internal float DotProduct(ReadOnlyMemory<float> v1, ReadOnlyMemory<float> v2)
    {
        if (v1.Length != v2.Length)
            throw new ArgumentException("Vectors must have the same length");

        return TensorPrimitives.Dot(v1.Span, v2.Span);
    }

    public IEnumerable<(T Entry, float Score)> Search<T>(float[] queryVector, int topK) where T : IEmbeddable
    {
        IEnumerable<(IEmbeddable Entry, ReadOnlyMemory<float> Vector)> typedItems = Items.Where(x => x.Entry is T);

        ReadOnlyMemory<float> normalisedQueryVector = NormaliseVector(queryVector);
        IEnumerable<(T Entry, float Score)> topItems
            = typedItems.Select(x => (Entry: (T)x.Entry, Score: DotProduct(x.Vector, normalisedQueryVector)))
                .OrderByDescending(x => x.Score)
                .Take(topK);

        return topItems;
    }
}