using System.Net;

namespace BackingTrack.Api.Tests.Integration;

// Fresh ApiFactory per test: each one exhausts a limiter, and that state must not leak between tests.
public class RateLimitPartitionTests
{
    private static async Task ExhaustPermitsAsync(HttpClient client)
    {
        for (int i = 0; i < ApiFactory.RateLimitPermits; i++)
            await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());
    }

    [Fact]
    public async Task Rejected_requests_carry_a_retry_after_header()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        await ExhaustPermitsAsync(client);

        HttpResponseMessage response = await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("60", Assert.Single(response.Headers.GetValues("Retry-After")));
    }

    [Fact]
    public async Task Requests_up_to_the_permit_limit_are_not_rejected()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        for (int i = 0; i < ApiFactory.RateLimitPermits; i++)
        {
            HttpResponseMessage response = await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
    }

    [Fact]
    public async Task Status_cancel_and_download_are_exempt_from_the_limit()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        await ExhaustPermitsAsync(client);

        for (int i = 0; i < ApiFactory.RateLimitPermits + 2; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/tracks/status/{Guid.NewGuid()}", TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/tracks/cancel/{Guid.NewGuid()}", content: null, TestContext.Current.CancellationToken)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/tracks/download/{Guid.NewGuid()}", TestContext.Current.CancellationToken)).StatusCode);
        }
    }

    [Fact]
    public async Task Each_api_key_has_its_own_limit()
    {
        using ApiFactory factory = new();
        using HttpClient validKeyClient = factory.CreateAuthenticatedClient();
        await ExhaustPermitsAsync(validKeyClient);
        using HttpClient otherKeyClient = factory.CreateClient();
        otherKeyClient.DefaultRequestHeaders.Add("X-API-Key", "some-other-key");

        HttpResponseMessage response = await otherKeyClient.PostGenerateAsync(TestRequests.ValidGenerateRequest());

        // Not 429: the other key is in its own partition. It is 401 because it isn't the configured key.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Requests_without_a_key_share_one_anonymous_limit_and_are_throttled_before_auth()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        for (int i = 0; i < ApiFactory.RateLimitPermits; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostGenerateAsync(TestRequests.ValidGenerateRequest())).StatusCode);

        HttpResponseMessage response = await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}