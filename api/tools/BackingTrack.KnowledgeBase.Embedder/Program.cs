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

builder.Services.AddSingleton<EmbeddingOrchestrator>();

IHost app = builder.Build();

ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();
EmbeddingOrchestrator orchestrator = app.Services.GetRequiredService<EmbeddingOrchestrator>();

orchestrator.ValidateForDuplicateIds();
IList<(string Id, VectorEntry VectorEntry)> chordRecordsToWrite = await orchestrator.GetChordRecordsToWriteAsync();
IList<(string Id, VectorEntry VectorEntry)> scaleRecordsToWrite = await orchestrator.GetScaleRecordsToWriteAsync();
IList<(string Id, VectorEntry VectorEntry)> styleRecordsToWrite = await orchestrator.GetStyleRecordsToWriteAsync();

await orchestrator.WriteRecords<ChordEntry>(chordRecordsToWrite);
await orchestrator.WriteRecords<ScaleEntry>(scaleRecordsToWrite);
await orchestrator.WriteRecords<StyleEntry>(styleRecordsToWrite);

logger.LogInformation("Embedding file generation complete");

return;