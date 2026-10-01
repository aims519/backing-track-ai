using BackingTrack.Generation.Jobs;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Api.Tests.Integration;

/// <summary>
/// Boots the real API pipeline (auth filter, rate limiter, exception handler, endpoints) with the
/// background job worker removed, so a generated job stays Pending and no LLM is ever called.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-api-key";

    /// <summary>Low enough to trip in a test without a loop of dozens of requests.</summary>
    public const int RateLimitPermits = 3;

    private readonly string _trackFolder = Directory.CreateTempSubdirectory("backing-track-tests").FullName;

    public string TrackFolder => _trackFolder;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting, not ConfigureAppConfiguration: Program reads RateLimit and Otel from
        // builder.Configuration while building services, before later config sources are layered in.
        builder.UseEnvironment("Development");
        builder.UseSetting("Auth:ApiKey", ApiKey);
        builder.UseSetting("RateLimit:PermitLimit", RateLimitPermits.ToString());
        builder.UseSetting("RateLimit:WindowSeconds", "60");
        builder.UseSetting("Otel:IsEnabled", "false");
        builder.UseSetting("TrackStorage:BaseFolderPath", _trackFolder);
        builder.UseSetting("VoyageAi:ApiKey", "unused");
        builder.UseSetting("OpenAi:ApiKey", "unused");

        builder.ConfigureServices(services =>
        {
            ServiceDescriptor? worker = services.SingleOrDefault(d => d.ImplementationType == typeof(GenerationJobWorkerHostedService));
            if (worker is not null)
                services.Remove(worker);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_trackFolder))
            Directory.Delete(_trackFolder, recursive: true);
    }

    /// <summary>A client that already carries the valid API key.</summary>
    public HttpClient CreateAuthenticatedClient()
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", ApiKey);
        return client;
    }
}