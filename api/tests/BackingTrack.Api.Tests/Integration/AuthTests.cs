using System.Net;

namespace BackingTrack.Api.Tests.Integration;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // status/cancel/download are exempt from rate limiting, so these can be repeated freely.
    public static TheoryData<string, string> ProtectedEndpoints => new()
    {
        { "GET", "/tracks/status/{id}" },
        { "POST", "/tracks/cancel/{id}" },
        { "GET", "/tracks/download/{id}" },
    };

    private static HttpRequestMessage BuildRequest(string method, string path, string? apiKey)
    {
        HttpRequestMessage request = new(new HttpMethod(method), path.Replace("{id}", Guid.NewGuid().ToString()));
        if (apiKey is not null)
            request.Headers.Add("X-API-Key", apiKey);
        return request;
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Missing_api_key_is_unauthorized(string method, string path)
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(BuildRequest(method, path, apiKey: null), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Wrong_api_key_is_unauthorized(string method, string path)
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(BuildRequest(method, path, "not-the-key"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Empty_api_key_is_unauthorized(string method, string path)
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(BuildRequest(method, path, ""), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_key_comparison_is_case_sensitive()
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.SendAsync(BuildRequest("GET", "/tracks/status/{id}", ApiFactory.ApiKey.ToUpperInvariant()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/healthz/live")]
    [InlineData("/healthz/ready")]
    public async Task Health_checks_do_not_require_an_api_key(string path)
    {
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}