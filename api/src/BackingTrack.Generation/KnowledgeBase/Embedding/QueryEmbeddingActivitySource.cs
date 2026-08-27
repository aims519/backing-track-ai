using System.Diagnostics;

namespace BackingTrack.Generation.KnowledgeBase.Embedding;

public static class QueryEmbeddingActivitySource
{
    public const string SourceName = "BackingTrack.QueryEmbedding";

    public static readonly ActivitySource Instance = new(SourceName);
}