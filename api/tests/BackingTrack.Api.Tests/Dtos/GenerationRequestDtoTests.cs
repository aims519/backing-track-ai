using BackingTrack.Api.Dtos;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

using Microsoft.AspNetCore.Mvc;

namespace BackingTrack.Api.Tests.Dtos;

public class GenerationRequestDtoTests
{
    private const string ValidDeviceToken = "abababababababababababababababababababababababababababababababab";

    private static ChordSymbolParser CreateParser()
    {
        (IEmbeddable Entry, float[] Vector)[] items =
        [
            (new ChordEntry { Id = "maj", Intervals = [0, 4, 7], Description = "Major triad" }, new float[] { 1f, 0f })
        ];
        return new ChordSymbolParser(new InMemoryVectorStore(items));
    }

    private static GenerationRequestDto CreateValidRequest(string? apnsDeviceToken) => new()
    {
        Mode = "Precise",
        Tempo = 120,
        Chords = ["Cmaj", "Gmaj"],
        KeyRoot = "C",
        KeyMode = "Ionian",
        Style = "rock",
        TimeSignatureBeatCount = 4,
        TimeSignatureBeatUnit = 4,
        ApnsDeviceToken = apnsDeviceToken
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(ValidDeviceToken)]
    [InlineData("ABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABAB")]
    public void Validate_AbsentOrWellFormedDeviceToken_IsValid(string? token)
    {
        ValidationProblemDetails? problems = CreateValidRequest(token).Validate(CreateParser());

        Assert.Null(problems);
    }

    [Theory]
    [InlineData("abababababababababababababababababababababababababababababababa")] // odd length
    [InlineData("ababababababababababababababababababababababababababababababab")] // too short
    [InlineData("gbababababababababababababababababababababababababababababababab")] // non-hex character
    [InlineData("../../../../3/device/ababababababababababababababababababababababa")] // path traversal
    public void Validate_MalformedDeviceToken_ReportsAnApnsDeviceTokenError(string token)
    {
        ValidationProblemDetails? problems = CreateValidRequest(token).Validate(CreateParser());

        Assert.NotNull(problems);
        Assert.Equal([nameof(GenerationRequestDto.ApnsDeviceToken)], problems.Errors.Keys);
    }

    [Fact]
    public void Validate_DeviceTokenLongerThanTheUpperBound_ReportsAnApnsDeviceTokenError()
    {
        ValidationProblemDetails? problems = CreateValidRequest(new string('a', 202)).Validate(CreateParser());

        Assert.NotNull(problems);
        Assert.Contains(nameof(GenerationRequestDto.ApnsDeviceToken), problems.Errors.Keys);
    }
}