using System.Net;
using System.Net.Http.Json;

using BackingTrack.Generation.Jobs;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Api.Tests.Integration;

// Completing the queue writer is how the host looks during shutdown, and the only way TryWrite
// returns false on the unbounded channel.
public class GenerateQueueUnavailableTests
{
    [Fact]
    public async Task Generate_returns_503_with_retry_after_when_the_queue_rejects_the_job()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        factory.Services.GetRequiredService<GenerationJobQueue>().Writer.Complete();

        HttpResponseMessage response = await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("30", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, problem?.Status);
    }

    [Fact]
    public async Task Generate_does_not_send_a_location_when_the_queue_rejects_the_job()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        factory.Services.GetRequiredService<GenerationJobQueue>().Writer.Complete();

        HttpResponseMessage response = await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());

        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Generate_removes_the_job_from_the_store_when_the_queue_rejects_it()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        factory.Services.GetRequiredService<GenerationJobQueue>().Writer.Complete();
        JobStore jobStore = factory.Services.GetRequiredService<JobStore>();

        await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());

        Assert.Equal(0, jobStore.JobCount);
    }

    // Guards the test above against passing vacuously - it would also hold if nothing were ever stored.
    [Fact]
    public async Task Generate_keeps_the_job_in_the_store_when_the_queue_accepts_it()
    {
        using ApiFactory factory = new();
        using HttpClient client = factory.CreateAuthenticatedClient();
        JobStore jobStore = factory.Services.GetRequiredService<JobStore>();

        await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());

        Assert.Equal(1, jobStore.JobCount);
    }
}