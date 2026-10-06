using System.Diagnostics;

using BackingTrack.Api.Extensions;

namespace BackingTrack.Api.Tests.Telemetry;

public sealed class HttpTelemetryRedactionTests : IDisposable
{
    private const string DeviceToken = "abababababababababababababababababababababababababababababababab";

    private readonly ActivitySource _source = new("BackingTrack.Tests.HttpRedaction");
    private readonly ActivityListener _listener;

    public HttpTelemetryRedactionTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source == _source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    /// <summary>An activity tagged the way the runtime's HttpClient instrumentation tags it: with the full request URL.</summary>
    private Activity StartHttpActivity(string url)
    {
        Uri uri = new(url);
        Activity activity = _source.StartActivity("POST")!;
        activity.SetTag("url.full", url);
        activity.SetTag("url.path", uri.AbsolutePath);
        activity.SetTag("server.address", uri.Host);
        activity.SetTag("http.request.method", "POST");
        return activity;
    }

    private static HttpRequestMessage RequestTo(string url) => new(HttpMethod.Post, url);

    [Theory]
    [InlineData("https://api.sandbox.push.apple.com")]
    [InlineData("https://api.push.apple.com")]
    public void Enrich_ApnsRequest_RemovesTheDeviceTokenFromEveryTag(string host)
    {
        string url = $"{host}/3/device/{DeviceToken}";
        using Activity activity = StartHttpActivity(url);

        HttpTelemetryRedaction.Enrich(activity, RequestTo(url));

        Assert.All(activity.TagObjects, tag => Assert.DoesNotContain(DeviceToken, tag.Value?.ToString() ?? ""));
    }

    [Fact]
    public void Enrich_ApnsRequest_KeepsTheHostAndTheRestOfTheTagsSoTheSpanStaysUseful()
    {
        string url = $"https://api.sandbox.push.apple.com/3/device/{DeviceToken}";
        using Activity activity = StartHttpActivity(url);

        HttpTelemetryRedaction.Enrich(activity, RequestTo(url));

        Assert.Contains("api.sandbox.push.apple.com", (string)activity.GetTagItem("url.full")!);
        Assert.Contains("/3/device/", (string)activity.GetTagItem("url.full")!);
        Assert.Equal("api.sandbox.push.apple.com", activity.GetTagItem("server.address"));
        Assert.Equal("POST", activity.GetTagItem("http.request.method"));
    }

    [Theory]
    [InlineData("https://api.voyageai.com/v1/embeddings")]
    [InlineData("https://api.openai.com/v1/responses")]
    public void Enrich_OtherHosts_LeavesTheTagsUntouched(string url)
    {
        using Activity activity = StartHttpActivity(url);

        HttpTelemetryRedaction.Enrich(activity, RequestTo(url));

        Assert.Equal(url, activity.GetTagItem("url.full"));
        Assert.Equal(new Uri(url).AbsolutePath, activity.GetTagItem("url.path"));
    }

    [Fact]
    public void Enrich_ApnsRequest_DoesNotChangeTheRequestItself()
    {
        // Only the telemetry is redacted - the request that actually goes to Apple must keep the real token.
        string url = $"https://api.sandbox.push.apple.com/3/device/{DeviceToken}";
        using Activity activity = StartHttpActivity(url);
        HttpRequestMessage request = RequestTo(url);

        HttpTelemetryRedaction.Enrich(activity, request);

        Assert.Equal(url, request.RequestUri!.ToString());
    }
}