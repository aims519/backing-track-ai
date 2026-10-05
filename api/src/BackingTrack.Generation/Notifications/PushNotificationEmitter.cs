using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using BackingTrack.Generation.Jobs;

using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Notifications;

internal class PushNotificationEmitter(HttpClient httpClient, ApnsTokenProvider tokenProvider, PushNotificationMetrics metrics, ILogger<PushNotificationEmitter> logger) : IPushNotificationEmitter
{
    private static readonly JobStates[] ValidStatesForNotification = [JobStates.Complete, JobStates.CompletedViaFallback, JobStates.Failed];

    public async Task SendApnsPushNotificationAsync(GenerationJob job)
    {
        if (job.ApnsDeviceToken is null)
        {
            logger.LogInformation("No ApnsDeviceToken found for the job, skipping send");
            return;
        }

        if (!ValidStatesForNotification.Contains(job.State))
        {
            logger.LogWarning("Push notification attempted for a job in an unsupported state: {State}", job.State);
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            string title = BuildAlertTitle(job);
            string message = BuildAlertBody(job);
            ApnsPayload payload = new(new ApnsAps(new ApnsAlert(title, message)), job.Id);

            using HttpRequestMessage request = new(HttpMethod.Post, $"/3/device/{job.ApnsDeviceToken}");
            request.Version = HttpVersion.Version20;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            request.Content = JsonContent.Create(payload, ApnsJsonContext.Default.ApnsPayload);
            request.Headers.Authorization = new AuthenticationHeaderValue("bearer", tokenProvider.GetToken());

            using HttpResponseMessage response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();

                logger.LogWarning("Failed to send apns push notification. Status code: {Code}. Reason: {Body}",
                    response.StatusCode, responseBody);

                string rejectionReason = ParseApnsRejectionReason(responseBody);
                metrics.RecordRejected(job.State, (int)response.StatusCode, rejectionReason, stopwatch.Elapsed);
                return;
            }

            metrics.RecordSent(job.State, stopwatch.Elapsed);
        }
        catch (TaskCanceledException)
        {
            metrics.RecordTimeout(job.State, stopwatch.Elapsed);
            logger.LogWarning("Push notification task cancelled");
        }
        catch (Exception ex)
        {
            metrics.RecordError(job.State, stopwatch.Elapsed);
            logger.LogError(ex, "Failed to send apns push notification");
        }
    }

    private string ParseApnsRejectionReason(string responseBody)
    {
        string rejectionReason;
        try
        {
            ApnsRejection? rejection =
                JsonSerializer.Deserialize<ApnsRejection>(responseBody, ApnsJsonContext.Default.ApnsRejection);
            rejectionReason = rejection?.Reason ?? "unknown";
        }
        catch (JsonException)
        {
            rejectionReason = "unknown";
        }

        return rejectionReason;
    }

    private static string BuildAlertTitle(GenerationJob job)
    {
        return job.State switch
        {
            JobStates.Complete => "Track Ready",
            JobStates.CompletedViaFallback => "Track Ready",
            JobStates.Failed => "Track Generation Failed",
            _ => string.Empty
        };
    }

    private static string BuildAlertBody(GenerationJob job)
    {
        string styleString = job.Style.Length > 50
            ? $"{job.Style[..50]}..."
            : job.Style;
        return $"{job.KeyRoot} {job.KeyMode} - {job.Tempo} BPM - {styleString}";
    }
}