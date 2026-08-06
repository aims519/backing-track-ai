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
        float[] normalisedVector = new float[vector.Length];
        float sumOfSquares = vector.Sum(x => x * x);
        
        for (int i = 0; i < vector.Length; i++)
        {
            normalisedVector[i] = vector[i] / (float)Math.Sqrt(sumOfSquares);
        }
        
        return normalisedVector;
    }
}