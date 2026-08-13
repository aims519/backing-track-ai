using System.Text.Json;
using BackingTrack.Core.Config;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.KnowledgeBase.Embedder;
using BackingTrack.KnowledgeBase.Embedder.VoyageAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("BackingTrack.KnowledgeBase.Embedder");

HostApplicationBuilder builder = new();
builder.Configuration.AddUserSecrets<Program>();

builder.Services.AddOptions();
builder.Services.Configure<VoyageAISettings>(builder.Configuration.GetSection("VoyageAI"));
builder.Services.AddHttpClient<IEmbeddingService, VoyageAiEmbeddingService>(client =>
{
    client.BaseAddress = new(builder.Configuration.GetValue<string>("VoyageAI:RestApiBaseUrl")!);
    client.DefaultRequestHeaders.Authorization = new("Bearer", builder.Configuration.GetValue<string>("VoyageAI:ApiKey"));
});

IHost app = builder.Build();

IEmbeddingService embeddingService = app.Services.GetRequiredService<IEmbeddingService>();
ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();

(IEnumerable<ChordEntry> chordEntries, IEnumerable<ScaleEntry> scaleEntries, IEnumerable<StyleEntry> styleEntries) = embeddingService.ParseRagFiles();
List<ChordEntry> chordEntryList = chordEntries.ToList();
List<ScaleEntry> scaleEntryList = scaleEntries.ToList();
List<StyleEntry> styleEntryList = styleEntries.ToList();

logger.LogInformation("Read raw content files.\nChords: {ChordItemCount}\nScales: {ScaleItemCount}\nStyles: {StyleItemCount}", 
    chordEntryList.Count,  scaleEntryList.Count, styleEntryList.Count);

string contentDirectory = KnowledgeBaseContentPaths.ResolveBaseDirectory();
contentDirectory = Path.GetFullPath(contentDirectory);

// Check for new/updated chords content
string chordOutputPath = Path.Combine(contentDirectory, "chords.vector.jsonl");
Dictionary<string, VectorEntry> existingChordVectorEntries = VectorEntryReader.ReadExisting(chordOutputPath, logger);

IReadOnlyList<ChordEntry> newOrUpdatedChords = VectorDiffHelper.GetNewOrUpdated(chordEntryList, existingChordVectorEntries).ToList();
(existingChordVectorEntries, int chordEntriesPruned) = existingChordVectorEntries.PruneVectorEntries(chordEntryList);

logger.LogInformation("New/Update Chord entries to embed: {Count}.\nChord entries to remove: {RemoveCount}",
    newOrUpdatedChords.Count, chordEntriesPruned);

// Get Chords Embeddings if necessary
await foreach ((IEmbeddable embeddable, float[] embedVector) in embeddingService.EmbedBatchAsync(
                   newOrUpdatedChords))
{
    VectorEntry vectorEntry = new(embeddable.Id,VectorDiffHelper.ComputeHash(embeddable.SearchText), embedVector);
    existingChordVectorEntries[vectorEntry.Id] = vectorEntry;
}

// Write Chords
await using StreamWriter chordsWriter = new(chordOutputPath, append: false);
foreach (KeyValuePair<string, VectorEntry> existingChordVectorEntry in existingChordVectorEntries)
{
    await chordsWriter.WriteLineAsync(JsonSerializer.Serialize(existingChordVectorEntry.Value));
}

// Check for new/updated scales content
string scalesOutputPath = Path.Combine(contentDirectory, "scales.vector.jsonl");
Dictionary<string, VectorEntry> existingScaleVectorEntries = VectorEntryReader.ReadExisting(scalesOutputPath, logger);

IReadOnlyList<ScaleEntry> newOrUpdatedScales = VectorDiffHelper.GetNewOrUpdated(scaleEntryList, existingScaleVectorEntries).ToList();
(existingScaleVectorEntries, int scaleEntriesPruned) = existingScaleVectorEntries.PruneVectorEntries(scaleEntryList);

logger.LogInformation("New/Update Scale entries to embed: {Count}.\nScale entries to remove: {RemoveCount}",
    newOrUpdatedScales.Count, scaleEntriesPruned);

// Get Scales Embeddings if necessary
await foreach ((IEmbeddable embeddable, float[] embedVector) in embeddingService.EmbedBatchAsync(
                   newOrUpdatedScales))
{
    VectorEntry vectorEntry = new(embeddable.Id,VectorDiffHelper.ComputeHash(embeddable.SearchText), embedVector);
    existingScaleVectorEntries[vectorEntry.Id] = vectorEntry;
}

// Write Scales
await using StreamWriter scalesWriter = new(scalesOutputPath, append: false);
foreach (KeyValuePair<string, VectorEntry> existingScaleVectorEntry in existingScaleVectorEntries)
{
    await scalesWriter.WriteLineAsync(JsonSerializer.Serialize(existingScaleVectorEntry.Value));
}

// Check for new/updated styles content
string stylesOutputPath = Path.Combine(contentDirectory, "styles.vector.jsonl");
Dictionary<string, VectorEntry> existingStyleVectorEntries = VectorEntryReader.ReadExisting(stylesOutputPath, logger);

IReadOnlyList<StyleEntry> newOrUpdatedStyles = VectorDiffHelper.GetNewOrUpdated(styleEntryList, existingStyleVectorEntries).ToList();
(existingStyleVectorEntries, int styleEntriesPruned) = existingStyleVectorEntries.PruneVectorEntries(styleEntryList);

logger.LogInformation("New/Update Style entries to embed: {Count}.\nStyle entries to remove: {RemoveCount}",
    newOrUpdatedStyles.Count, styleEntriesPruned);

// Get Styles Embeddings if necessary
await foreach ((IEmbeddable embeddable, float[] embedVector) in embeddingService.EmbedBatchAsync(
                   newOrUpdatedStyles))
{
    VectorEntry vectorEntry = new(embeddable.Id,VectorDiffHelper.ComputeHash(embeddable.SearchText), embedVector);
    existingStyleVectorEntries[vectorEntry.Id] = vectorEntry;
}

// Write Styles
await using StreamWriter stylesWriter = new(stylesOutputPath, append: false);
foreach (KeyValuePair<string, VectorEntry> existingStyleVectorEntry in existingStyleVectorEntries)
{
    await stylesWriter.WriteLineAsync(JsonSerializer.Serialize(existingStyleVectorEntry.Value));
}

return;