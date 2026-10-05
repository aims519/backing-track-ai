using System.Diagnostics;

namespace BackingTrack.Api.Extensions;

internal static class HttpTelemetryRedaction
{
    private const string DeviceSegment = "/3/device/";

    public static void Enrich(Activity activity, HttpRequestMessage request)
    {
        Uri? uri = request.RequestUri;
        if (uri is null || !uri.Host.EndsWith("push.apple.com", StringComparison.OrdinalIgnoreCase))
            return;

        int start = uri.AbsolutePath.IndexOf(DeviceSegment, StringComparison.Ordinal);
        if (start < 0)
            return;

        string token = uri.AbsolutePath[(start + DeviceSegment.Length)..];
        if (token.Length == 0)
            return;

        // ToList(): SetTag changes the tag collection, which can't happen while enumerating it
        foreach (KeyValuePair<string, object?> tag in activity.TagObjects.ToList())
        {
            if (tag.Value is string value && value.Contains(token))
                activity.SetTag(tag.Key, value.Replace(token, "{redacted}"));
        }
    }
}