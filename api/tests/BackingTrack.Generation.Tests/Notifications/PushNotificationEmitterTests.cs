using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

using BackingTrack.Generation.Jobs;
using BackingTrack.Generation.Notifications;
using BackingTrack.Generation.Tests.Workflows;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BackingTrack.Generation.Tests.Notifications;

public sealed class PushNotificationEmitterTests : IDisposable
{
    private const string DeviceToken = "b76252068fdfa84cf6b03d46495b43f7599b05a82e0c079b0d24ed52ca6d96ba";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ApnsTokenProvider _tokenProvider;
    private readonly RecordingHandler _handler = new();
    private readonly PushNotificationEmitter _emitter;

    public PushNotificationEmitterTests()
    {
        _tokenProvider = ApnsTestFixtures.CreateTokenProvider(_key, new FakeTimeProvider(DateTimeOffset.UtcNow));
        HttpClient httpClient = new(_handler) { BaseAddress = new Uri("https://api.sandbox.push.apple.com") };
        _emitter = new PushNotificationEmitter(httpClient, _tokenProvider, NullLogger<PushNotificationEmitter>.Instance);
    }

    public void Dispose()
    {
        _tokenProvider.Dispose();
        _key.Dispose();
        _handler.Dispose();
    }

    private static GenerationJob CreateJob(JobStates state, string? deviceToken = DeviceToken, string style = "rock") =>
        WorkflowTestFixtures.CreateGenerationJob() with { State = state, ApnsDeviceToken = deviceToken, Style = style };

    [Fact]
    public async Task Send_TerminalJobWithToken_PostsToTheDevicePathOverHttp2()
    {
        await _emitter.SendApnsPushNotificationAsync(CreateJob(JobStates.Complete));

        HttpRequestMessage request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"/3/device/{DeviceToken}", request.RequestUri!.AbsolutePath);
        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionExact, request.VersionPolicy);
    }

    [Fact]
    public async Task Send_TerminalJobWithToken_SendsTheProviderTokenAsBearer()
    {
        await _emitter.SendApnsPushNotificationAsync(CreateJob(JobStates.Complete));

        HttpRequestMessage request = Assert.Single(_handler.Requests);
        Assert.Equal("bearer", request.Headers.Authorization?.Scheme, ignoreCase: true);
        Assert.Equal(_tokenProvider.GetToken(), request.Headers.Authorization?.Parameter);
    }

    [Theory]
    [InlineData(JobStates.Complete, "Track Ready")]
    [InlineData(JobStates.CompletedViaFallback, "Track Ready")]
    [InlineData(JobStates.Failed, "Track Generation Failed")]
    public async Task Send_TerminalJob_PayloadHasApsAlertAndJobId(JobStates state, string expectedTitle)
    {
        GenerationJob job = CreateJob(state);

        await _emitter.SendApnsPushNotificationAsync(job);

        JsonElement payload = JsonDocument.Parse(Assert.Single(_handler.Bodies)).RootElement;
        JsonElement aps = payload.GetProperty("aps");
        Assert.Equal(expectedTitle, aps.GetProperty("alert").GetProperty("title").GetString());
        Assert.Equal("C Ionian - 120 BPM - rock", aps.GetProperty("alert").GetProperty("body").GetString());
        Assert.Equal("default", aps.GetProperty("sound").GetString());
        Assert.Equal(job.Id, payload.GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task Send_LongStyle_TruncatesItInTheAlertBody()
    {
        string longStyle = new('x', 80);

        await _emitter.SendApnsPushNotificationAsync(CreateJob(JobStates.Complete, style: longStyle));

        JsonElement payload = JsonDocument.Parse(Assert.Single(_handler.Bodies)).RootElement;
        string? body = payload.GetProperty("aps").GetProperty("alert").GetProperty("body").GetString();
        Assert.EndsWith($" - {new string('x', 50)}...", body);
    }

    [Fact]
    public async Task Send_JobWithoutDeviceToken_SendsNothing()
    {
        await _emitter.SendApnsPushNotificationAsync(CreateJob(JobStates.Complete, deviceToken: null));

        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData(JobStates.Pending)]
    [InlineData(JobStates.InProgress)]
    [InlineData(JobStates.Cancelled)]
    public async Task Send_NonNotifiableState_SendsNothing(JobStates state)
    {
        await _emitter.SendApnsPushNotificationAsync(CreateJob(state));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Send_ApnsRejectsThePush_DoesNotThrow()
    {
        _handler.Response = () => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"reason":"BadDeviceToken"}""")
        };

        Exception? ex = await Record.ExceptionAsync(() => _emitter.SendApnsPushNotificationAsync(CreateJob(JobStates.Complete)));

        Assert.Null(ex);
    }

    [Fact]
    public async Task Send_TransportFailure_DoesNotThrow()
    {
        _handler.Response = () => throw new HttpRequestException("connection refused");

        Exception? ex = await Record.ExceptionAsync(() => _emitter.SendApnsPushNotificationAsync(CreateJob(JobStates.Complete)));

        Assert.Null(ex);
    }

    [Fact]
    public async Task Send_Timeout_DoesNotThrow()
    {
        _handler.Response = () => throw new TaskCanceledException();

        Exception? ex = await Record.ExceptionAsync(() => _emitter.SendApnsPushNotificationAsync(CreateJob(JobStates.Complete)));

        Assert.Null(ex);
    }

    /// <summary>Captures each request and its body up front - the emitter disposes the request
    /// (and so its content) once SendAsync returns.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        public Func<HttpResponseMessage> Response { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return Response();
        }
    }
}