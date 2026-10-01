using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using BackingTrack.Api.Dtos;
using BackingTrack.Api.Serialization;

namespace BackingTrack.Api.Tests.Integration;

public class SmokeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Host_starts_and_rejects_unauthenticated_requests()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync($"/tracks/status/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Host_starts_and_accepts_authenticated_requests()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/tracks/status/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Valid_generate_requests_creates_pending_job()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        GenerationRequestDto request = new()
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
        HttpContent content = JsonContent.Create(request, AppJsonSerializerContext.Default.GenerationRequestDto);
        HttpResponseMessage generateResponse = await client.PostAsync($"/tracks/generate", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, generateResponse.StatusCode);

        GenerationJobDto? acceptedJob =
            await generateResponse.Content.ReadFromJsonAsync<GenerationJobDto>(TestContext.Current.CancellationToken);

        HttpResponseMessage statusResponse = await client.GetAsync($"/tracks/status/{acceptedJob!.Id}", TestContext.Current.CancellationToken);
        GenerationJobDto? statusJob = await statusResponse.Content.ReadFromJsonAsync<GenerationJobDto>(TestContext.Current.CancellationToken);

        Assert.NotNull(statusJob);
        Assert.Equal("Pending", statusJob.Status);
    }
}