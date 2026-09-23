using BackingTrack.Api.Dtos;
using BackingTrack.Generation.KnowledgeBase;
using BackingTrack.Generation.KnowledgeBase.Embedding;
using BackingTrack.Generation.KnowledgeBase.Types;

using Microsoft.AspNetCore.Mvc;

namespace BackingTrack.Api.Tests.Dtos;

public class GenerationRequestDtoTests
{
    private const string ValidDeviceToken = "b76252068fdfa84cf6b03d46495b43f7599b05a82e0c079b0d24ed52ca6d96ba";

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
    [InlineData("B76252068FDFA84CF6B03D46495B43F7599B05A82E0C079B0D24ED52CA6D96BA")]
    public void Validate_AbsentOrWellFormedDeviceToken_IsValid(string? token)
    {
        ValidationProblemDetails? problems = CreateValidRequest(token).Validate(CreateParser());

        Assert.Null(problems);
    }

    [Theory]
    [InlineData("b76252068fdfa84cf6b03d46495b43f7599b05a82e0c079b0d24ed52ca6d96b")] // odd length
    [InlineData("b76252068fdfa84cf6b03d46495b43f7599b05a82e0c079b0d24ed52ca6d96")] // too short
    [InlineData("g76252068fdfa84cf6b03d46495b43f7599b05a82e0c079b0d24ed52ca6d96ba")] // non-hex character
    [InlineData("../../../../3/device/b76252068fdfa84cf6b03d46495b43f7599b05a82e0c0")] // path traversal
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