using BackingTrack.Generation.KnowledgeBase.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.KnowledgeBase.Embedding;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeBaseVectorStore(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            ILogger<InMemoryVectorStore> logger = sp.GetRequiredService<ILogger<InMemoryVectorStore>>();
            
            string contentDirectory = Path.GetFullPath(KnowledgeBaseContentPaths.ResolveBaseDirectory());

            logger.LogInformation("Initialising vector store from content at {ContentPath}", contentDirectory);

            string chordsContentPath = Path.Combine(contentDirectory, KnowledgeBaseContentPaths.ResolveRawContentFilePath<ChordEntry>());
            string scalesContentPath = Path.Combine(contentDirectory, KnowledgeBaseContentPaths.ResolveRawContentFilePath<ScaleEntry>());
            string stylesContentPath = Path.Combine(contentDirectory, KnowledgeBaseContentPaths.ResolveRawContentFilePath<StyleEntry>());

            string chordsVectorPath = Path.Combine(contentDirectory, KnowledgeBaseContentPaths.ResolveVectorFilePath<ChordEntry>());
            string scalesVectorPath = Path.Combine(contentDirectory, KnowledgeBaseContentPaths.ResolveVectorFilePath<ScaleEntry>());
            string stylesVectorPath = Path.Combine(contentDirectory,KnowledgeBaseContentPaths.ResolveVectorFilePath<StyleEntry>());

            IEnumerable<ChordEntry> chordEntries = JsonlReader.Parse(chordsContentPath, KnowledgeBaseJsonContext.Default.ChordEntry);
            IEnumerable<ScaleEntry> scaleEntries = JsonlReader.Parse(scalesContentPath, KnowledgeBaseJsonContext.Default.ScaleEntry);
            IEnumerable<StyleEntry> styleEntries = JsonlReader.Parse(stylesContentPath, KnowledgeBaseJsonContext.Default.StyleEntry);

            IReadOnlyList<IEmbeddable> allEntries = [.. chordEntries, .. scaleEntries, .. styleEntries];

            Dictionary<string, VectorEntry> chordVectorEntries = VectorEntryReader.ReadExisting(chordsVectorPath, logger);
            Dictionary<string, VectorEntry> scaleVectorEntries = VectorEntryReader.ReadExisting(scalesVectorPath, logger);
            Dictionary<string, VectorEntry> styleVectorEntries = VectorEntryReader.ReadExisting(stylesVectorPath, logger);

            Dictionary<string, VectorEntry> allVectorEntries = chordVectorEntries
                    .Concat(scaleVectorEntries)
                    .Concat(styleVectorEntries)
                    .ToDictionary(x => x.Key, x => x.Value) ;

            foreach (IEmbeddable missingVectorEntry in allEntries.Where(e => !allVectorEntries.ContainsKey(e.Id)))
            {
                logger.LogWarning("Entry with Id, '{Entry}', has no corresponding embedding vector", missingVectorEntry.Id);
            }
            IReadOnlyList<(IEmbeddable entry, float[] vector)> storeEntries = allEntries
                .Where(e => allVectorEntries.ContainsKey(e.Id))
                .Select(e => (e, allVectorEntries[e.Id].Vector)).ToList();
            
            logger.LogInformation("Initialised {ItemCount} entries in the vector store", storeEntries.Count);
                return new InMemoryVectorStore(storeEntries);
            });
            
        return services;
    }
}