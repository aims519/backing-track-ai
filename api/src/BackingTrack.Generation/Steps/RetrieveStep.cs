using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

namespace BackingTrack.Generation.Steps;

public class RetrieveStep(InMemoryVectorStore vectorStore, IQueryEmbeddingService queryEmbeddingService)
    : IRetrieveStep
{
    public async Task<StyleEntry?> RetrieveTopMatchingStyleAsync(string styleQuery, CancellationToken cancellationToken = default)
    {
        float[] queryVector = await queryEmbeddingService.EmbedQueryAsync(styleQuery, cancellationToken);

        IEnumerable<(StyleEntry Entry, float Score)> results = vectorStore.Search<StyleEntry>(queryVector, 1);

        return results.FirstOrDefault().Entry;
    }
}