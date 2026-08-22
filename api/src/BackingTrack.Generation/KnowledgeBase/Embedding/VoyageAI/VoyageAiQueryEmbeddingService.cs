using System.Net.Http.Json;
using BackingTrack.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.KnowledgeBase.Embedding.VoyageAI;

public class VoyageAiQueryEmbeddingService(
    ILogger<VoyageAiQueryEmbeddingService> logger,
    IOptions<VoyageAISettings> voyageAiSettings,
    HttpClient httpClient)
    : IQueryEmbeddingService
{
    private readonly VoyageAISettings _voyageAiSettings = voyageAiSettings.Value;

    public async Task<float[]> EmbedQueryAsync(string queryText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            throw new ArgumentException("Query text must be provided", nameof(queryText));

        VoyageAIQueryEmbeddingRequestDto requestDto = new()
        {
            Input = [queryText],
            Model = _voyageAiSettings.EmbeddingModel,
            InputType = "query"
        };

        HttpResponseMessage responseMessage = await httpClient.PostAsJsonAsync(
            "embeddings",
            requestDto,
            VoyageAIQueryJsonContext.Default.VoyageAIQueryEmbeddingRequestDto,
            cancellationToken);

        if (!responseMessage.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Voyage AI query embedding request failed. Response code {responseMessage.StatusCode}. Reason: {responseMessage.ReasonPhrase}");
        }

        VoyageAIQueryEmbeddingResponseDto? responseDto;
        try
        {
            responseDto = await responseMessage.Content.ReadFromJsonAsync(
                VoyageAIQueryJsonContext.Default.VoyageAIQueryEmbeddingResponseDto,
                cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Error deserialising Voyage AI query embedding response");
            throw;
        }

        if (responseDto == null || responseDto.Data.Length == 0)
            throw new InvalidOperationException("Voyage AI returned no embedding data for the query");

        return responseDto.Data[0].Embedding;
    }
}
