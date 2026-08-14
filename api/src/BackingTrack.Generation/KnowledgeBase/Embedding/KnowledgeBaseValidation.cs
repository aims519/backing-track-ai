using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.KnowledgeBase.Embedding;

public static class KnowledgeBaseValidation
{
    public static HashSet<string> FindDuplicateContentIds(params IEnumerable<IEmbeddable>[] entryListsToCheck)
    {
        Dictionary<string, IEmbeddable> fullDictionary = new();
        HashSet<string> duplicateIds = new();

        foreach (IEnumerable<IEmbeddable> embeddableList in entryListsToCheck)
        {
            foreach (IEmbeddable embeddable in embeddableList)
            {
                if (fullDictionary.ContainsKey(embeddable.Id))
                {
                    duplicateIds.Add(embeddable.Id);
                    continue;
                }
                
                fullDictionary.TryAdd(embeddable.Id, embeddable);
            }
        }

        return duplicateIds;
    }
}