using BackingTrack.Core.Config;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Embedding.VoyageAi;
using BackingTrack.Generation.KnowledgeBase.Types;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.KnowledgeBase;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeBase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VoyageAiSettings>()
            .Bind(configuration.GetSection("VoyageAi"))
            .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "VoyageAi:ApiKey must be configured")
            .Validate(s => !string.IsNullOrWhiteSpace(s.RestApiBaseUrl), "VoyageAi:RestApiBaseUrl must be configured")
            .Validate(s => !string.IsNullOrWhiteSpace(s.EmbeddingModel), "VoyageAi:EmbeddingModel must be configured")
            .ValidateOnStart();

        services.AddKnowledgeBaseVectorStore();
        services.AddHostedService<VectorStoreWarmup>();

        services.AddSingleton<ChordSymbolParser>();
        services.AddSingleton<ScaleMatcher>();

        services.AddHttpClient<IQueryEmbeddingService, VoyageAiQueryEmbeddingService>((sp, client) =>
        {
            VoyageAiSettings settings = sp.GetRequiredService<IOptions<VoyageAiSettings>>().Value;
            client.BaseAddress = new(settings.RestApiBaseUrl);
            client.DefaultRequestHeaders.Authorization = new("Bearer", settings.ApiKey);
        })
        .AddStandardResilienceHandler(options => options.Retry.ShouldRetryAfterHeader = true);

        return services;
    }

    private static IServiceCollection AddKnowledgeBaseVectorStore(this IServiceCollection services)
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
            string stylesVectorPath = Path.Combine(contentDirectory, KnowledgeBaseContentPaths.ResolveVectorFilePath<StyleEntry>());

            IList<ChordEntry> chordEntries = JsonlReader.Parse(chordsContentPath, KnowledgeBaseJsonContext.Default.ChordEntry).ToList();
            IList<ScaleEntry> scaleEntries = JsonlReader.Parse(scalesContentPath, KnowledgeBaseJsonContext.Default.ScaleEntry).ToList();
            IList<StyleEntry> styleEntries = JsonlReader.Parse(stylesContentPath, KnowledgeBaseJsonContext.Default.StyleEntry).ToList();

            HashSet<string> duplicateIds = KnowledgeBaseValidation.FindDuplicateContentIds(chordEntries, scaleEntries, styleEntries);
            if (duplicateIds.Count > 0)
            {
                throw new InvalidOperationException($"Duplicate entry ids are not allowed across all record types. Duplicates found: {string.Join(", ", duplicateIds)}");
            }

            IReadOnlyList<IEmbeddable> allEntries = [.. chordEntries, .. scaleEntries, .. styleEntries];

            Dictionary<string, VectorEntry> chordVectorEntries = VectorEntryReader.ReadExisting(chordsVectorPath, logger);
            Dictionary<string, VectorEntry> scaleVectorEntries = VectorEntryReader.ReadExisting(scalesVectorPath, logger);
            Dictionary<string, VectorEntry> styleVectorEntries = VectorEntryReader.ReadExisting(stylesVectorPath, logger);

            Dictionary<string, VectorEntry> allVectorEntries = chordVectorEntries
                    .Concat(scaleVectorEntries)
                    .Concat(styleVectorEntries)
                    .ToDictionary(x => x.Key, x => x.Value);

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