using System.Net;
using System.Net.Http.Json;

using BackingTrack.Api.Dtos;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace BackingTrack.Api.Tests.Integration;

// Drives the real eviction hosted service by advancing a fake clock, rather than waiting out the TTL.
// The clock starts at real "now" because jobs stamp EndTime from DateTimeOffset.UtcNow, not TimeProvider.
public class JobEvictionTests
{
    private static readonly TimeSpan PastTtlAndSweepInterval = TimeSpan.FromHours(2);

    private static WebApplicationFactory<Program> WithClock(ApiFactory baseFactory, FakeTimeProvider time) =>
        baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(time);
        }));

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", ApiFactory.ApiKey);
        return client;
    }

    private static async Task<Guid> GenerateAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostGenerateAsync(TestRequests.ValidGenerateRequest());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GenerationJobDto>(TestContext.Current.CancellationToken))!.Id;
    }

    private static async Task<HttpStatusCode> StatusOfAsync(HttpClient client, Guid jobId) =>
        (await client.GetAsync($"/tracks/status/{jobId}", TestContext.Current.CancellationToken)).StatusCode;

    // The sweep runs on its own task, and a tick advanced before its timer exists is missed, so keep
    // advancing until the job disappears instead of assuming one Advance is enough.
    private static async Task AdvanceUntilGoneAsync(HttpClient client, FakeTimeProvider time, Guid jobId)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (await StatusOfAsync(client, jobId) != HttpStatusCode.NotFound)
        {
            time.Advance(PastTtlAndSweepInterval);
            await Task.Delay(20, timeout.Token);
        }
    }

    [Fact]
    public async Task A_finished_job_is_evicted_once_the_ttl_has_passed_while_a_pending_job_is_kept()
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        using ApiFactory baseFactory = new();
        using WebApplicationFactory<Program> factory = WithClock(baseFactory, time);
        using HttpClient client = CreateAuthenticatedClient(factory);

        Guid cancelledJob = await GenerateAsync(client);
        Guid pendingJob = await GenerateAsync(client);
        await client.PostAsync($"/tracks/cancel/{cancelledJob}", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(client, cancelledJob));

        await AdvanceUntilGoneAsync(client, time, cancelledJob);

        Assert.Equal(HttpStatusCode.NotFound, await StatusOfAsync(client, cancelledJob));
        Assert.Equal(HttpStatusCode.OK, await StatusOfAsync(client, pendingJob));
    }

    [Fact]
    public async Task Cancelling_an_evicted_job_returns_not_found()
    {
        FakeTimeProvider time = new(DateTimeOffset.UtcNow);
        using ApiFactory baseFactory = new();
        using WebApplicationFactory<Program> factory = WithClock(baseFactory, time);
        using HttpClient client = CreateAuthenticatedClient(factory);
        Guid jobId = await GenerateAsync(client);
        await client.PostAsync($"/tracks/cancel/{jobId}", content: null, TestContext.Current.CancellationToken);
        await AdvanceUntilGoneAsync(client, time, jobId);

        HttpResponseMessage cancelAgain = await client.PostAsync($"/tracks/cancel/{jobId}", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, cancelAgain.StatusCode);
    }
}