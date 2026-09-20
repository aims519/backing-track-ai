using System.Text.Json;
using System.Text.Json.Nodes;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Steps.Critic;

public class CriticAiClient([FromKeyedServices("critic")] IChatClient chatClient,
    CriticProviderInfo providerInfo, ScaleMatcher scaleMatcher) : ICriticAiClient
{
    private const string SubmitVerdictFunctionName = "submit_verdict";

    private static readonly JsonElement CriticVerdictSubmissionSchema = AIJsonUtilities.CreateJsonSchema(
        typeof(CriticVerdictSubmission),
        description: "The critic's per-dimension verdict for this candidate track.",
        serializerOptions: new JsonSerializerOptions { TypeInfoResolver = CriticStepJsonContext.Default });

    public async Task<CriticVerdictSubmission> GenerateCriticVerdictSubmissionAsync(CandidateTrack candidateTrack,
        GenerationJob generationJob,
        MatchedStyle matchedStyle,
        IReadOnlyList<MatchedChord> matchedChords,
        IReadOnlyList<MatchedScale> matchedScales,
        CancellationToken cancellationToken = default)
    {
        string prompt = GenerateRequestPrompt(candidateTrack, generationJob, matchedStyle, matchedChords, matchedScales);
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
        IReadOnlyList<CriticMatchedScale> criticMatchedScales = matchedScales
            .Select(matchedScale => new CriticMatchedScale(matchedScale, scaleMatcher))
            .ToList();

        return $"""
                - CandidateTrack: {JsonSerializer.Serialize(new CriticCandidateTrack(candidateTrack, generationJob.TimeSignatureBeatCount), CriticStepJsonContext.Default.CriticCandidateTrack)}
                - GenerationJob: {JsonSerializer.Serialize(generationJob, GenerationJobJsonContext.Default.GenerationJob)}
                - MatchedStyle: {JsonSerializer.Serialize(matchedStyle, KnowledgeBaseJsonContext.Default.MatchedStyle)}
                - MatchedChords: {JsonSerializer.Serialize(matchedChords, KnowledgeBaseJsonContext.Default.IReadOnlyListMatchedChord)}
                - MatchedScales: {JsonSerializer.Serialize(criticMatchedScales, CriticStepJsonContext.Default.IReadOnlyListCriticMatchedScale)}
                """;
    }

    private CriticVerdictSubmission ParseFunctionCallContentArguments(IDictionary<string, object?> functionCallArguments)
    {
        JsonObject node = new();
        foreach ((string key, object? value) in functionCallArguments)
        {
            if (value is not JsonElement element)
                throw new InvalidOperationException("Failed to parse the submit_verdict tool call arguments.");

            node[key] = JsonNode.Parse(element.GetRawText());
        }

        CriticVerdictSubmission verdictSubmission = JsonSerializer.Deserialize(node.ToJsonString(), CriticStepJsonContext.Default.CriticVerdictSubmission)
                                                    ?? throw new InvalidOperationException("Failed to parse the submit_verdict tool call arguments.");

        if (verdictSubmission.DimensionAssessments is null)
            throw new InvalidOperationException("Failed to parse the submit_verdict tool call arguments.");

        return verdictSubmission;
    }

    private ChatOptions BuildChatOptions()
    {
        ChatOptions options = new()
        {
            Tools =
            [
                AIFunctionFactory.CreateDeclaration(SubmitVerdictFunctionName,
                    "Submit your review findings. Call exactly once, once every dimension has been assessed.",
                    CriticVerdictSubmissionSchema)
            ],
            ToolMode = ChatToolMode.RequireSpecific(SubmitVerdictFunctionName),
            MaxOutputTokens = providerInfo.MaxOutputTokens,
        };
        options.ApplyServiceTier(providerInfo.ServiceTier);

        return options;
    }
}