using System.Text.Json;
using System.Text.Json.Nodes;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Steps.Critic;

public class CriticAiClient([FromKeyedServices("critic")] IChatClient chatClient,
    CriticProviderInfo providerInfo) : ICriticAiClient
{
    private const string SubmitVerdictFunctionName = "submit_verdict";
    public async Task<CriticVerdictSubmission> GenerateCriticVertictSubmissionAsync(CandidateTrack candidateTrack,
        GenerationJob generationJob, 
        MatchedStyle matchedStyle,
        IReadOnlyList<MatchedChord> matchedChords, 
        IReadOnlyList<MatchedScale> matchedScales, 
        CancellationToken cancellationToken = default)
    {
        string prompt = GenerateRequestPrompt(candidateTrack, generationJob, matchedStyle,  matchedChords, matchedScales);
        ChatMessage[] messages =
        [
            new(ChatRole.System, CriticStepPrompts.SystemInstructions),
            new(ChatRole.User, prompt)
        ];
        
        ChatOptions chatOptions = BuildChatOptions();
        ChatResponse chatResponse = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);

        FunctionCallContent? content = chatResponse
            .Messages.SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .FirstOrDefault(c => c.Name == SubmitVerdictFunctionName);
        
        if (content?.Arguments == null)
            throw new InvalidOperationException("Unable to find verdict function call");
        
        return ParseFunctionCallContentArguments(content.Arguments);
    }
    
    private string GenerateRequestPrompt(CandidateTrack candidateTrack,
        GenerationJob generationJob, MatchedStyle matchedStyle,
        IReadOnlyList<MatchedChord> matchedChords, IReadOnlyList<MatchedScale> matchedScales)
    {
        return $"""
                - CandidateTrack: {JsonSerializer.Serialize(candidateTrack, CandidateTrackJsonContext.Default.CandidateTrack)}
                - GenerationJob: {JsonSerializer.Serialize(generationJob, GenerationJobJsonContext.Default.GenerationJob)}
                - MatchedStyle: {JsonSerializer.Serialize(matchedStyle, KnowledgeBaseJsonContext.Default.MatchedStyle)}
                - MatchedChords: {JsonSerializer.Serialize(matchedChords, KnowledgeBaseJsonContext.Default.IReadOnlyListMatchedChord)}
                - MatchedScales: {JsonSerializer.Serialize(matchedScales, KnowledgeBaseJsonContext.Default.IReadOnlyListMatchedScale)}
                """;
    }

    private CriticVerdictSubmission ParseFunctionCallContentArguments(IDictionary<string, object?> functionCallArguments)
    {
        JsonObject node = new();
        foreach ((string key, object? value) in functionCallArguments)
            node[key] = value is JsonElement element ? JsonNode.Parse(element.GetRawText()) : null; 
        
        CriticVerdictSubmission verdictSubmission = JsonSerializer.Deserialize(node.ToJsonString(), CriticStepJsonContext.Default.CriticVerdictSubmission)
                                                    ?? throw new InvalidOperationException("Could not parse the submit_verdict tool call arguments.");
        
        return verdictSubmission;
    }

    private ChatOptions BuildChatOptions()
    {
        JsonElement schema = AIJsonUtilities.CreateJsonSchema(
            typeof(CriticVerdictSubmission),
            description: "The critic's per-dimension verdict for this candidate track.",
            serializerOptions: new JsonSerializerOptions { TypeInfoResolver = CriticStepJsonContext.Default });
        
        ChatOptions options = new()
        {
            Tools =
            [
                AIFunctionFactory.CreateDeclaration(SubmitVerdictFunctionName,
                    "Submit your review findings. Call exactly once, once every dimension has been assessed.", 
                    schema)
            ],
            ToolMode = ChatToolMode.RequireSpecific(SubmitVerdictFunctionName),
            MaxOutputTokens = providerInfo.MaxOutputTokens,
        };

        return options;
    }
}