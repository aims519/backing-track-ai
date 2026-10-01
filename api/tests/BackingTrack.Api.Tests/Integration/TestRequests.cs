using System.Net.Http.Json;

using BackingTrack.Api.Dtos;
using BackingTrack.Api.Serialization;

namespace BackingTrack.Api.Tests.Integration;

internal static class TestRequests
{
    public static GenerationRequestDto ValidGenerateRequest() => new()
    {
        Mode = "Precise",
        Tempo = 100,
        Chords = ["C", "D"],
        KeyRoot = "C",
        KeyMode = "Ionian",
        TimeSignatureBeatCount = 4,
        TimeSignatureBeatUnit = 4,
        Style = "Pop"
    };

    public static Task<HttpResponseMessage> PostGenerateAsync(this HttpClient client, GenerationRequestDto request) =>
        client.PostAsync("/tracks/generate", JsonContent.Create(request, AppJsonSerializerContext.Default.GenerationRequestDto), TestContext.Current.CancellationToken);

    public static Task<HttpResponseMessage> PostRawJsonAsync(this HttpClient client, string path, string json) =>
        client.PostAsync(path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);
}