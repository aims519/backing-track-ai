using System.Net;
using System.Net.Http.Json;

using BackingTrack.Api.Dtos;

namespace BackingTrack.Api.Tests.Integration;

// The job worker is removed in ApiFactory, so a generated job stays Pending until cancelled.
// A fresh factory per test keeps each one under the rate limit.
public class JobLifecycleTests
{
    private static async Task<(HttpResponseMessage Response, GenerationJobDto Job)> GenerateAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        GenerationJobDto job = (await response.Content.ReadFromJsonAsync<GenerationJobDto>(TestContext.Current.CancellationToken))!;
        return (response, job);
    }

    private static async Task<GenerationJobDto> GetStatusAsync(HttpClient client, Guid jobId)
    {
        HttpResponseMessage response = await client.GetAsync($"/tracks/status/{jobId}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GenerationJobDto>(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task Generate_returns_accepted_with_a_location_pointing_at_the_job()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();

        (HttpResponseMessage response, GenerationJobDto job) = await GenerateAsync(client);

        Assert.Equal($"/tracks/status/{job.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal("Pending", job.Status);
    }

    [Fact]
    public async Task Following_the_location_header_returns_the_pending_job()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        (HttpResponseMessage response, GenerationJobDto job) = await GenerateAsync(client);

        HttpResponseMessage statusResponse = await client.GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        GenerationJobDto status = (await statusResponse.Content.ReadFromJsonAsync<GenerationJobDto>(TestContext.Current.CancellationToken))!;
        Assert.Equal(job.Id, status.Id);
        Assert.Equal("Pending", status.Status);
        Assert.Null(status.EndTime);
    }

    [Fact]
    public async Task Cancelling_a_pending_job_marks_it_cancelled()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        (_, GenerationJobDto job) = await GenerateAsync(client);

        HttpResponseMessage cancelResponse = await client.PostAsync($"/tracks/cancel/{job.Id}", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);
        GenerationJobDto status = await GetStatusAsync(client, job.Id);
        Assert.Equal("Cancelled", status.Status);
        Assert.NotNull(status.EndTime);
    }

    [Fact]
    public async Task Cancelling_twice_is_idempotent()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        (_, GenerationJobDto job) = await GenerateAsync(client);
        await client.PostAsync($"/tracks/cancel/{job.Id}", content: null, TestContext.Current.CancellationToken);
        GenerationJobDto afterFirst = await GetStatusAsync(client, job.Id);

        HttpResponseMessage secondCancel = await client.PostAsync($"/tracks/cancel/{job.Id}", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, secondCancel.StatusCode);
        GenerationJobDto afterSecond = await GetStatusAsync(client, job.Id);
        Assert.Equal("Cancelled", afterSecond.Status);
        Assert.Equal(afterFirst.EndTime, afterSecond.EndTime);
    }

    [Fact]
    public async Task Cancelling_one_job_leaves_another_pending()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        (_, GenerationJobDto first) = await GenerateAsync(client);
        (_, GenerationJobDto second) = await GenerateAsync(client);

        await client.PostAsync($"/tracks/cancel/{first.Id}", content: null, TestContext.Current.CancellationToken);

        Assert.Equal("Cancelled", (await GetStatusAsync(client, first.Id)).Status);
        Assert.Equal("Pending", (await GetStatusAsync(client, second.Id)).Status);
    }
}