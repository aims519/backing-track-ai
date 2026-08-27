namespace BackingTrack.Generation.KnowledgeBase.Embedding;

public interface IQueryEmbeddingService
{
    Task<float[]> EmbedQueryAsync(string queryText, CancellationToken cancellationToken = default);
}