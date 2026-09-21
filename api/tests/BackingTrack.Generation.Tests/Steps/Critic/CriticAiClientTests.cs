using System.Text.Json;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;

using Microsoft.Extensions.AI;

using NSubstitute;

namespace BackingTrack.Generation.Tests.Steps.Critic;

public class CriticAiClientTests
{
    private const string SubmitVerdictFunctionName = "submit_verdict";

    private static readonly CandidateTrack DummyTrack = new(16, [], [], []);
    private static readonly MatchedStyle DummyStyle = new(new StyleEntry { Id = "rock", Description = "Rock" });
    private static readonly GenerationJob DummyJob = new()
    {
        Id = Guid.NewGuid(),
        State = JobStates.InProgress,
        StartTime = DateTimeOffset.UtcNow,
        Mode = GenerationMode.Precise,
        Tempo = 120,
        Chords = ["C"],
        KeyRoot = "C",
        KeyMode = KeyMode.Ionian,
        Style = "rock",
        TimeSignatureBeatCount = 4,
        TimeSignatureBeatUnit = 4,
        FeedbackItems = []
    };

    // Mirrors how the real OpenAI connector (OpenAIClientExtensions.ParseCallContent) builds
    // FunctionCallContent.Arguments: JsonSerializer.Deserialize into IDictionary<string, object>,
    // which leaves every value as a boxed JsonElement since `object` has no shape of its own.
    // Building the fixture this way (rather than hand-assembling a dictionary of plain strings/bools)
    // is what actually exercises CriticAiClient's JsonObject/JsonNode reconstruction path faithfully.
    private static IDictionary<string, object?> BuildRealisticFunctionCallArguments(CriticVerdictSubmission submission)
    {
        string json = JsonSerializer.Serialize(submission, CriticStepJsonContext.Default.CriticVerdictSubmission);
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
    }

    private static IChatClient CreateChatClientReturning(ChatResponse response)
    {
        IChatClient chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(response);
        return chatClient;
    }

    private static Task<CriticVerdictSubmission> GenerateAsync(IChatClient chatClient) =>
        new CriticAiClient(chatClient, new CriticProviderInfo("OpenAi", "test-model", 4096), new ScaleMatcher())
            .GenerateCriticVerdictSubmissionAsync(DummyTrack, DummyJob, DummyStyle, [], [], TestContext.Current.CancellationToken);

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ResponseContainsSubmitVerdictCall_ParsesArgumentsIntoSubmission()
    {
        CriticVerdictSubmission expected = new([
            new(CriticDimension.ChordToneAdherence, false, "root notes buried in the voicing", "maj"),
            new(CriticDimension.VoiceLeading, true, null, null)
        ]);
        FunctionCallContent call = new("call-1", SubmitVerdictFunctionName, BuildRealisticFunctionCallArguments(expected));
        ChatResponse response = new(new ChatMessage(ChatRole.Assistant, [call]));

        CriticVerdictSubmission result = await GenerateAsync(CreateChatClientReturning(response));

        Assert.Equal(expected.DimensionAssessments.Count, result.DimensionAssessments.Count);
        Assert.Equal(expected.DimensionAssessments[0], result.DimensionAssessments[0]);
        Assert.Equal(expected.DimensionAssessments[1], result.DimensionAssessments[1]);
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ResponseHasNoSubmitVerdictCall_ThrowsInvalidOperationException()
    {
        ChatResponse response = new(new ChatMessage(ChatRole.Assistant, "I have reviewed the track."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => GenerateAsync(CreateChatClientReturning(response)));
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ResponseHasADifferentToolCall_ThrowsInvalidOperationException()
    {
        FunctionCallContent call = new("call-1", "some_other_tool", new Dictionary<string, object?>());
        ChatResponse response = new(new ChatMessage(ChatRole.Assistant, [call]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => GenerateAsync(CreateChatClientReturning(response)));
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ForcesTheSubmitVerdictToolOnEveryCall()
    {
        CriticVerdictSubmission submission = new([new(CriticDimension.RhythmAndFeel, true, null, null)]);
        FunctionCallContent call = new("call-1", SubmitVerdictFunctionName, BuildRealisticFunctionCallArguments(submission));
        ChatResponse response = new(new ChatMessage(ChatRole.Assistant, [call]));
        IChatClient chatClient = CreateChatClientReturning(response);

        await GenerateAsync(chatClient);

        await chatClient.Received(1).GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Is<ChatOptions>(o => ForcesSubmitVerdictTool(o)),
            Arg.Any<CancellationToken>());
    }

    private static bool ForcesSubmitVerdictTool(ChatOptions options) =>
        options.ToolMode is RequiredChatToolMode { RequiredFunctionName: SubmitVerdictFunctionName } &&
        options.Tools != null && options.Tools.Any(t => t.Name == SubmitVerdictFunctionName);

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ArgumentsOmitDimensionAssessments_ThrowsInvalidOperationException()
    {
        // The tool schema doesn't force DimensionAssessments to be present (System.Text.Json's
        // constructor-based deserialization just leaves a missing parameter at its default, null,
        // for a non-required record parameter) - an empty arguments object reproduces that directly.
        FunctionCallContent call = new("call-1", SubmitVerdictFunctionName, new Dictionary<string, object?>());
        ChatResponse response = new(new ChatMessage(ChatRole.Assistant, [call]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => GenerateAsync(CreateChatClientReturning(response)));
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ArgumentValueIsNotAJsonElement_ThrowsInvalidOperationException()
    {
        // FunctionCallContent.Arguments values are expected to always be JsonElement (see the
        // BuildRealisticFunctionCallArguments comment above) - if a connector or provider ever hands
        // back an already-materialized CLR value instead, parsing must fail loudly, not silently drop it.
        Dictionary<string, object?> arguments = new() { ["DimensionAssessments"] = "not a JsonElement" };
        FunctionCallContent call = new("call-1", SubmitVerdictFunctionName, arguments);
        ChatResponse response = new(new ChatMessage(ChatRole.Assistant, [call]));

        await Assert.ThrowsAsync<InvalidOperationException>(() => GenerateAsync(CreateChatClientReturning(response)));
    }
}