using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.KnowledgeBase.Embedding;

public class InMemoryVectorStore(IEnumerable<(IEmbeddable Entry, float[] Vector)> items)
{
    public readonly (IEmbeddable Entry, float[] Vector)[] Items = Normalise(items);

    private static (IEmbeddable Entry, float[] Vector)[] Normalise(IEnumerable<(IEmbeddable Entry, float[] Vector)> items)
    {
        return items.Select(item => (item.Entry, NormaliseVector(item.Vector))).ToArray();
    }

    internal static float[] NormaliseVector(float[] vector)
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

    internal float DotProduct(float[] v1, float[] v2)
    {
        if(v1.Length != v2.Length)
            throw new ArgumentException("Vectors must have the same length");
        
        float dotProduct = 0f;
        for (int i = 0; i < v1.Length; i++)
        {
            dotProduct += v1[i] * v2[i];
        }
        
        return dotProduct;
    }

    internal float CosineSimilarity(float[] v1, float[] v2)
    {
        float dotProduct = DotProduct(v1, v2);
        float v1Mag = VectorMagnitude(v1);
        float v2Mag = VectorMagnitude(v2);
        
        return dotProduct / (v1Mag * v2Mag);
    }

    public IEnumerable<(T Entry, float Score)> Search<T>(float[] queryVector, int topK) where T : IEmbeddable
    {
        IEnumerable<(IEmbeddable Entry, float[] Vector)> typedItems = Items.Where(x => x.Entry.GetType() == typeof(T));

        float[] normalisedQueryVector = NormaliseVector(queryVector);
        IEnumerable<(T Entry, float Score)> topItems 
            = typedItems.Select(x => (Entry: (T)x.Entry, Score: CosineSimilarity(x.Vector, normalisedQueryVector)))
                .OrderByDescending(x => x.Score)
                .Take(topK);

        return topItems;
    }
}