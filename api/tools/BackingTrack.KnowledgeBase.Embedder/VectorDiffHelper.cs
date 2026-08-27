using System.Security.Cryptography;
using System.Text;

using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.KnowledgeBase.Embedder;

internal static class VectorDiffHelper
{
    internal static (Dictionary<string, VectorEntry> prunedVectorEntries, int entriesPruned) PruneVectorEntries(this Dictionary<string, VectorEntry> existingVectorEntries, IReadOnlyList<IEmbeddable> embeddables)
    {
        HashSet<string> currentIds = embeddables.Select(x => x.Id).ToHashSet();
        int existingEntryCount = existingVectorEntries.Count;
        existingVectorEntries = existingVectorEntries
            .Where(kvp => currentIds.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return (existingVectorEntries, existingEntryCount - existingVectorEntries.Count);
    }

    internal static IEnumerable<T> GetNewOrUpdated<T>(
        IEnumerable<T> embeddables, Dictionary<string, VectorEntry> existingVectorEntries) where T : IEmbeddable
    {
        IEnumerable<T> newOrUpdated = embeddables
            .Where(entry =>
                (existingVectorEntries.ContainsKey(entry.Id) && HasContentChanged(entry, existingVectorEntries[entry.Id].Hash))
                || !existingVectorEntries.ContainsKey(entry.Id)
            );

        return newOrUpdated;
    }

    internal static string ComputeHash(string text)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }

    internal static bool HasContentChanged(IEmbeddable embeddable, string existingContentHash)
    {
        return !string.Equals(ComputeHash(embeddable.SearchText), existingContentHash, StringComparison.Ordinal);
    }
}