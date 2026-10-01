using System.Net;

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
}