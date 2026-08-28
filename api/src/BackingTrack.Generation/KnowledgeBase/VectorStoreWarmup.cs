using BackingTrack.Generation.KnowledgeBase.Embedding;

using Microsoft.Extensions.Hosting;

namespace BackingTrack.Generation.KnowledgeBase;

/// <summary>
/// Forces the <see cref="InMemoryVectorStore"/> singleton to be built during host
/// startup rather than on the first request: the KB content is read, validated for
/// duplicate ids, and embedded up front, and startup fails fast if content is missing.
/// Resolving the store in the constructor is what triggers its factory.
/// </summary>
internal sealed class VectorStoreWarmup(InMemoryVectorStore vectorStore) : IHostedService
{
    private readonly InMemoryVectorStore _vectorStore = vectorStore;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}