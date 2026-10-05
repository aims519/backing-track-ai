using System.Net;
using System.Security.Cryptography;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Notifications;
using BackingTrack.Generation.Tests.Workflows;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Tests.Notifications;

/// <summary>
/// The APNs URL contains the device token (/3/device/{token}), and HttpClient's built-in request logging writes
/// the URL. Goes through the real registration so it covers whatever loggers AddPushNotifications leaves on the
/// client, with only the network layer swapped for a stub.
/// </summary>
public sealed class PushNotificationLoggingTests : IDisposable
{
    private const string DeviceToken = "b76252068fdfa84cf6b03d46495b43f7599b05a82e0c079b0d24ed52ca6d96ba";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly CapturingLoggerProvider _logs = new();

    public void Dispose() => _key.Dispose();

    private async Task SendPushAsync(HttpStatusCode apnsResponse)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Apns:ServerUrl"] = "https://api.sandbox.push.apple.com",
                ["Apns:BundleId"] = "com.example.backingtrack",
                ["Apns:P8Key"] = _key.ExportPkcs8PrivateKeyPem(),
                ["Apns:P8KeyId"] = ApnsTestFixtures.KeyId,
                ["Apns:TeamId"] = ApnsTestFixtures.TeamId,
            })
            .Build();

        ServiceCollection services = new();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(_logs);
        });
        services.AddPushNotifications(configuration);

        // A typed client is named after its service type; extra configuration for that name replaces the network
        // layer while leaving every handler AddPushNotifications added (including any logging handlers) in place.
        services.AddHttpClient(nameof(IPushNotificationEmitter))
            .ConfigurePrimaryHttpMessageHandler(() => new StubHandler(apnsResponse));

        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IPushNotificationEmitter emitter = scope.ServiceProvider.GetRequiredService<IPushNotificationEmitter>();

        await emitter.SendApnsPushNotificationAsync(
            WorkflowTestFixtures.CreateGenerationJob() with { State = JobStates.Complete, ApnsDeviceToken = DeviceToken });
    }

    [Fact]
    public async Task Send_ApnsAccepts_NoLogLineContainsTheDeviceToken()
    {
        await SendPushAsync(HttpStatusCode.OK);

        Assert.All(_logs.Entries, entry => Assert.DoesNotContain(DeviceToken, entry));
    }

    [Fact]
    public async Task Send_ApnsRejects_NoLogLineContainsTheDeviceToken()
    {
        // The rejection path logs a warning of its own, so this also proves the capture is really seeing the logs
        // rather than passing because nothing was captured.
        await SendPushAsync(HttpStatusCode.BadRequest);

        Assert.Contains(_logs.Entries, entry => entry.Contains("Failed to send apns push notification"));
        Assert.All(_logs.Entries, entry => Assert.DoesNotContain(DeviceToken, entry));
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("""{"reason":"BadDeviceToken"}""") });
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_entries)
                    return [.. _entries];
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(string category, List<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // The formatted message plus the structured values, since the URL may only be in the latter.
                string structured = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}"))
                    : "";
                lock (entries)
                    entries.Add($"{category}: {formatter(state, exception)} {structured}{exception}");
            }
        }
    }
}