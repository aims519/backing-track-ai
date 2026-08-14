using System.Text.Json;

using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

using Microsoft.Extensions.Logging;

namespace BackingTrack.KnowledgeBase.Embedder;

internal class EmbeddingOrchestrator
{
    private readonly string _contentDirectory = Path.GetFullPath(KnowledgeBaseContentPaths.ResolveSourceContentDirectory());
    private readonly ILogger<EmbeddingOrchestrator> _logger;
    private readonly IEmbeddingService _embeddingService;

    private readonly (IEnumerable<ChordEntry> chordEntries, IEnumerable<ScaleEntry> scaleEntries, IEnumerable<StyleEntry>
        styleEntries) _contentEntries;

    public EmbeddingOrchestrator(ILogger<EmbeddingOrchestrator> logger, IEmbeddingService embeddingService)
    {
        _logger = logger;
        _embeddingService = embeddingService;
        _contentEntries = _embeddingService.ParseRagFiles();
        
        _logger.LogInformation("Read raw content files.\nChords: {ChordItemCount}\nScales: {ScaleItemCount}\nStyles: {StyleItemCount}", 
            _contentEntries.chordEntries.Count(),  _contentEntries.scaleEntries.Count(), _contentEntries.styleEntries.Count());
    }

    internal Task<IList<(string Id, VectorEntry VectorEntry)>> GetChordRecordsToWriteAsync() => GetRecordsToWriteAsync(_contentEntries.chordEntries.ToList());
    
    internal Task<IList<(string Id, VectorEntry VectorEntry)>> GetScaleRecordsToWriteAsync() => GetRecordsToWriteAsync(_contentEntries.scaleEntries.ToList());
    
    internal Task<IList<(string Id, VectorEntry VectorEntry)>> GetStyleRecordsToWriteAsync() => GetRecordsToWriteAsync(_contentEntries.styleEntries.ToList());

    internal async Task WriteRecords<T>(IList<(string Id, VectorEntry VectorEntry)> recordsToWrite) where T : class, IEmbeddable
    {
        string outputPath  = Path.Combine(_contentDirectory, KnowledgeBaseContentPaths.ResolveVectorFilePath<T>());
        _logger.LogInformation("Writing {Count} {Type} records to {Path}...", recordsToWrite.Count,  typeof(T).Name, outputPath);

        await using StreamWriter writer = new(outputPath, append: false);
        foreach ((string Id, VectorEntry VectorEntry) vectorEntry in recordsToWrite)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(vectorEntry.VectorEntry));
        }
    }

    internal void ValidateForDuplicateIds()
    {
        HashSet<string> duplicateIds = KnowledgeBaseValidation.FindDuplicateContentIds(_contentEntries.chordEntries, _contentEntries.scaleEntries, _contentEntries.styleEntries);
        if (duplicateIds.Count > 0)
        {
            throw new InvalidOperationException($"Duplicate entry ids are not allowed across all record types. Duplicates found: {string.Join(", ", duplicateIds)}");
        }
    }
    
    private async Task<IList<(string Id, VectorEntry VectorEntry)>> GetRecordsToWriteAsync<T>(IReadOnlyList<T> rawContentEntries) where T : class, IEmbeddable
    {
        string vectorFilePath = Path.Combine(_contentDirectory, KnowledgeBaseContentPaths.ResolveVectorFilePath<T>());
        Dictionary<string, VectorEntry> existingVectorEntries = VectorEntryReader.ReadExisting(vectorFilePath, _logger);

        List<T> newOrUpdated = VectorDiffHelper.GetNewOrUpdated(rawContentEntries, existingVectorEntries).ToList();
        (existingVectorEntries, int entriesPruned) = existingVectorEntries.PruneVectorEntries(rawContentEntries);

        _logger.LogInformation("New/Updated {NewUpdatedType} entries to embed: {Count}.\n{RemoveType} entries to remove: {RemoveCount}",
            typeof(T).Name, newOrUpdated.Count,  typeof(T).Name, entriesPruned);

        // Fetch Chords Embeddings if necessary
        await foreach ((IEmbeddable embeddable, float[] embedVector) in _embeddingService.EmbedBatchAsync(newOrUpdated))
        {
            VectorEntry vectorEntry = new(embeddable.Id,VectorDiffHelper.ComputeHash(embeddable.SearchText), embedVector);
            existingVectorEntries[vectorEntry.Id] = vectorEntry;
        }

        return existingVectorEntries.Select(x => (x.Key, x.Value)).ToList();
    }
}