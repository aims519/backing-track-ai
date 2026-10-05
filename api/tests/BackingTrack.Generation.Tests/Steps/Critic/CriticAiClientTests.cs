using System.Diagnostics;
using System.Text.Json;

using BackingTrack.Core.Config;
using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Types;
using BackingTrack.Generation.Steps.Critic;
using BackingTrack.Generation.Steps.Types;
using BackingTrack.Generation.Tests.Jobs;

using Microsoft.Extensions.AI;

using NSubstitute;

namespace BackingTrack.Generation.Tests.Steps.Critic;

[Collection(CriticMetricsCollection.Name)]
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

    private static async Task<CriticVerdictSubmission> GenerateAsync(IChatClient chatClient, OpenAiServiceTier? serviceTier = null)
    {
        using CriticMetrics metrics = new();
        return await new CriticAiClient(chatClient, new CriticProviderInfo("OpenAi", "test-model", 4096, serviceTier), new ScaleMatcher(), metrics)
            .GenerateCriticVerdictSubmissionAsync(DummyTrack, DummyJob, DummyStyle, [], [], TestContext.Current.CancellationToken);
    }

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

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_Success_RecordsCallDurationTokensAndSuccessOutcome()
    {
        ChatResponse response = ValidVerdictResponse();
        response.Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 45 };
        using MeasurementCapture calls = new("critic.calls", CriticMetrics.MeterName);
        using MeasurementCapture duration = new("critic.call.duration", CriticMetrics.MeterName);
        using MeasurementCapture input = new("critic.call.input_tokens", CriticMetrics.MeterName);
        using MeasurementCapture output = new("critic.call.output_tokens", CriticMetrics.MeterName);

        await GenerateAsync(CreateChatClientReturning(response));

        CapturedMeasurement call = Assert.Single(calls.Measurements);
        Assert.Equal("success", call.Tags["outcome"]);
        Assert.Equal("OpenAi", call.Tags["provider"]);
        Assert.Equal("test-model", call.Tags["model"]);
        Assert.Single(duration.Measurements);
        Assert.Equal(120, Assert.Single(input.Measurements).Value);
        Assert.Equal(45, Assert.Single(output.Measurements).Value);
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ChatClientThrows_RecordsFailureAndRethrows()
    {
        IChatClient chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns<ChatResponse>(_ => throw new HttpRequestException("openai down"));
        using MeasurementCapture calls = new("critic.calls", CriticMetrics.MeterName);
        using MeasurementCapture input = new("critic.call.input_tokens", CriticMetrics.MeterName);

        await Assert.ThrowsAsync<HttpRequestException>(() => GenerateAsync(chatClient));

        Assert.Equal("failure", Assert.Single(calls.Measurements).Tags["outcome"]);
        Assert.Empty(input.Measurements);
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ResponseHasNoVerdict_RecordsFailureWithTheTokensItCost()
    {
        // The call itself succeeded, but it produced nothing usable, so the caller sees a failure
        // (and the workflow fails the job); the metric must agree with that, and the tokens were still spent.
        ChatResponse response = new(new ChatMessage(ChatRole.Assistant, "I have reviewed the track."))
        {
            Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 45 }
        };
        using MeasurementCapture calls = new("critic.calls", CriticMetrics.MeterName);
        using MeasurementCapture input = new("critic.call.input_tokens", CriticMetrics.MeterName);
        using MeasurementCapture output = new("critic.call.output_tokens", CriticMetrics.MeterName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => GenerateAsync(CreateChatClientReturning(response)));

        Assert.Equal("failure", Assert.Single(calls.Measurements).Tags["outcome"]);
        Assert.Equal(120, Assert.Single(input.Measurements).Value);
        Assert.Equal(45, Assert.Single(output.Measurements).Value);
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ResponseHasNoVerdict_TagsTheErrorActivityWithTokens()
    {
        ChatResponse response = new(new ChatMessage(ChatRole.Assistant, "I have reviewed the track."))
        {
            Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 45 }
        };
        using ActivityCapture activities = new(CriticActivitySource.SourceName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => GenerateAsync(CreateChatClientReturning(response)));

        Activity activity = Assert.Single(activities.Activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(120L, activity.GetTagItem("critic.input_tokens"));
        Assert.Equal(45L, activity.GetTagItem("critic.output_tokens"));
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_Success_StartsAnActivityTaggedWithProviderModelAndTokens()
    {
        ChatResponse response = ValidVerdictResponse();
        response.Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 45 };
        using ActivityCapture activities = new(CriticActivitySource.SourceName);

        await GenerateAsync(CreateChatClientReturning(response));

        Activity activity = Assert.Single(activities.Activities);
        Assert.Equal("OpenAi", activity.GetTagItem("critic.provider"));
        Assert.Equal("test-model", activity.GetTagItem("critic.model"));
        Assert.Equal(120L, activity.GetTagItem("critic.input_tokens"));
        Assert.Equal(45L, activity.GetTagItem("critic.output_tokens"));
        Assert.NotEqual(ActivityStatusCode.Error, activity.Status);
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ChatClientThrows_MarksTheActivityAsError()
    {
        IChatClient chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns<ChatResponse>(_ => throw new HttpRequestException("openai down"));
        using ActivityCapture activities = new(CriticActivitySource.SourceName);

        await Assert.ThrowsAsync<HttpRequestException>(() => GenerateAsync(chatClient));

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(activities.Activities).Status);
    }

    private static ChatResponse ValidVerdictResponse()
    {
        CriticVerdictSubmission submission = new([new(CriticDimension.RhythmAndFeel, true, null, null)]);
        FunctionCallContent call = new("call-1", SubmitVerdictFunctionName, BuildRealisticFunctionCallArguments(submission));
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, [call]));
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ServiceTierConfigured_TagsMetricsAndActivity()
    {
        using MeasurementCapture calls = new("critic.calls", CriticMetrics.MeterName);
        using ActivityCapture activities = new(CriticActivitySource.SourceName);

        await GenerateAsync(CreateChatClientReturning(ValidVerdictResponse()), OpenAiServiceTier.Flex);

        Assert.Equal("flex", Assert.Single(calls.Measurements).Tags["service_tier"]);
        Assert.Equal("flex", Assert.Single(activities.Activities).GetTagItem("critic.service_tier"));
    }

    [Fact]
    public async Task GenerateCriticVertictSubmissionAsync_ChatClientThrows_FailureIsTaggedWithServiceTier()
    {
        IChatClient chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns<ChatResponse>(_ => throw new HttpRequestException("openai down"));
        using MeasurementCapture calls = new("critic.calls", CriticMetrics.MeterName);

        await Assert.ThrowsAsync<HttpRequestException>(() => GenerateAsync(chatClient, OpenAiServiceTier.Flex));

        Assert.Equal("flex", Assert.Single(calls.Measurements).Tags["service_tier"]);
    }
}