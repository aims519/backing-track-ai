using System.Net;
using System.Net.Http.Json;

using BackingTrack.Api.Dtos;
using BackingTrack.Api.Serialization;

namespace BackingTrack.Api.Tests.Integration;

// Own class, so its own ApiFactory and rate limiter: a shared fixture would let this test's requests
// use up the permits of any other test that generates under the same API key.
public class RateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Too_many_generate_requests_are_rejected_due_to_rate_limiting()
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

        for (int i = 1; i <= ApiFactory.RateLimitPermits; i++)
        {
            HttpResponseMessage _ = await client.PostAsync($"/tracks/generate", content, TestContext.Current.CancellationToken);
        }

        HttpResponseMessage response = await client.PostAsync($"/tracks/generate", content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}