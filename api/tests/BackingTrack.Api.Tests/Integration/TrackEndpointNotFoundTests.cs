using System.Net;

namespace BackingTrack.Api.Tests.Integration;

public class TrackEndpointNotFoundTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Status_of_an_unknown_job_is_not_found()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/tracks/status/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_of_an_unknown_job_is_not_found()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.PostAsync($"/tracks/cancel/{Guid.NewGuid()}", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_of_an_unknown_track_is_not_found()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/tracks/download/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/tracks/status/not-a-guid")]
    [InlineData("POST", "/tracks/cancel/not-a-guid")]
    [InlineData("GET", "/tracks/download/not-a-guid")]
    public async Task Non_guid_ids_do_not_match_a_route(string method, string path)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_of_a_stored_track_returns_the_file()
    {
        Guid trackId = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(factory.TrackFolder, $"{trackId}.json"), "{\"ok\":true}", TestContext.Current.CancellationToken);
        using HttpClient client = factory.CreateAuthenticatedClient();

        HttpResponseMessage response = await client.GetAsync($"/tracks/download/{trackId}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"ok\":true}", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}