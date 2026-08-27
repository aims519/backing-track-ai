using System.Text;
using System.Text.Json;

using BackingTrack.Core.Config;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using KnowledgeBaseJsonContext = BackingTrack.Generation.KnowledgeBase.Embedding.KnowledgeBaseJsonContext;

namespace BackingTrack.KnowledgeBase.Embedder.VoyageAI;

public class VoyageAiEmbeddingService(
    ILogger<VoyageAiEmbeddingService> logger,
    IOptions<VoyageAISettings> voyageAiSettings,
    HttpClient httpClient)
    : IEmbeddingService
{
    private readonly VoyageAISettings _voyageAISettings = voyageAiSettings.Value;
    private const string ScalesFileName = "scales.jsonl";
    private const string StylesFileName = "styles.jsonl";
    private const string ChordsFileName = "chords.jsonl";


    public (IEnumerable<ChordEntry> chordEntries, IEnumerable<ScaleEntry> scaleEntries, IEnumerable<StyleEntry> styleEntries) ParseRagFiles()
    {
        string contentDirectory = KnowledgeBaseContentPaths.ResolveSourceContentDirectory();
        contentDirectory = Path.GetFullPath(contentDirectory);

        string chordsPath = Path.Combine(contentDirectory, ChordsFileName);
        string scalesPath = Path.Combine(contentDirectory, ScalesFileName);
        string stylesPath = Path.Combine(contentDirectory, StylesFileName);

        IEnumerable<ChordEntry> chordEntries = JsonlReader.Parse<ChordEntry>(chordsPath, KnowledgeBaseJsonContext.Default.ChordEntry);
        IEnumerable<ScaleEntry> scaleEntries = JsonlReader.Parse<ScaleEntry>(scalesPath, KnowledgeBaseJsonContext.Default.ScaleEntry);
        IEnumerable<StyleEntry> styleEntries = JsonlReader.Parse<StyleEntry>(stylesPath, KnowledgeBaseJsonContext.Default.StyleEntry);

        return (chordEntries, scaleEntries, styleEntries);
    }

    public async IAsyncEnumerable<(IEmbeddable Embeddable, float[] EmbedVector)> EmbedBatchAsync(IReadOnlyList<IEmbeddable> embeddables)
    {
        if (embeddables.Count == 0)
        {
            logger.LogInformation("No embeddables provided, skipping call to Voyage AI");
            yield break;
        }

        object requestBody = new
        {
            input = embeddables.Select(x => x.SearchText).ToArray(),
            model = _voyageAISettings.EmbeddingModel,
            input_type = "document"
        };

        HttpContent content =
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        HttpResponseMessage responseMessage = await httpClient.PostAsync("embeddings", content);
        if (!responseMessage.IsSuccessStatusCode)
        {
            throw new Exception(
                $"Voyage AI embeddings request failed. Response code {responseMessage.StatusCode}. Reason: {responseMessage.ReasonPhrase}");
        }

        VoyageAIEmbeddingResponseDto voyageAiResponse;
        try
        {
            string responseJson = await responseMessage.Content.ReadAsStringAsync();
            voyageAiResponse = JsonSerializer.Deserialize<VoyageAIEmbeddingResponseDto>(responseJson)
                ?? throw new JsonException("Voyage AI response deserialised to null");
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Error deserialising embedding data");
            throw;
        }

        if (voyageAiResponse.Data.Length != embeddables.Count)
        {
            throw new InvalidOperationException(
                $"Expected {embeddables.Count} embeddings from Voyage AI but received {voyageAiResponse.Data.Length}");
        }

        Dictionary<int, float[]> vectorsByRequestIndex = voyageAiResponse.Data.ToDictionary(d => d.Index, d => d.Embedding);

        for (int i = 0; i < embeddables.Count; i++)
        {
            yield return (embeddables[i], vectorsByRequestIndex[i]);
        }
    }
}