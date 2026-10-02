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
    public static int RateLimitPermits = 3;

    // Program reads RateLimit and Otel eagerly from builder.Configuration, before ConfigureAppConfiguration
    // sources exist, so those two have to arrive as environment variables - the one source that is both in
    // place by then and layered above appsettings.Development.json. Constant values, so process-wide is safe.
    static ApiFactory()
    {
        Environment.SetEnvironmentVariable("RateLimit__PermitLimit", RateLimitPermits.ToString());
        Environment.SetEnvironmentVariable("RateLimit__WindowSeconds", "60");
        Environment.SetEnvironmentVariable("Otel__IsEnabled", "false");

        // AddPushNotifications also reads its section eagerly, and a developer's user secrets usually hold real
        // APNs credentials that make it register the real emitter, so locally the tests would take a different
        // path from CI (which has no secrets). Making the config unusable pins every machine to the NoOp emitter.
        // User secrets are added after environment variables, so they win for any key they define: pin keys that
        // normally come from appsettings.json (ServerUrl, BundleId), not the credentials (TeamId, P8Key*) that live
        // in secrets. A whitespace value is needed - the binder ignores an empty one.
        Environment.SetEnvironmentVariable("Apns__ServerUrl", " ");
        Environment.SetEnvironmentVariable("Apns__BundleId", " ");
    }

    private readonly string _trackFolder = Directory.CreateTempSubdirectory("backing-track-tests").FullName;

    public string TrackFolder => _trackFolder;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // An in-memory source added here is layered last, so it beats appsettings.Development.json and
        // the user secrets that Program adds unconditionally - the dev machine's real Auth:ApiKey would
        // otherwise win over the test key. Plain UseSetting values sit below both of those.
        // Only valid for settings read lazily via IOptions; see the static constructor for the eager ones.
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:ApiKey"] = ApiKey,
            ["TrackStorage:BaseFolderPath"] = _trackFolder,
            ["VoyageAi:ApiKey"] = "unused",
            ["OpenAi:ApiKey"] = "unused",
        }));

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