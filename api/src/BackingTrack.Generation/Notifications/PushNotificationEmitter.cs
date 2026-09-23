using System.Net.Http.Json;
using System.Runtime.InteropServices.JavaScript;

using BackingTrack.Generation.Jobs;

using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Notifications;

internal class PushNotificationEmitter(HttpClient httpClient, ILogger<PushNotificationEmitter> logger) : IPushNotificationEmitter
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

        try
        {
            string title = BuildAlertTitle(job);
            string message = BuildAlertBody(job);
            ApnsPayload payload = new(new ApnsAps(new ApnsAlert(title, message)), job.Id);
            JsonContent content = JsonContent.Create(payload, ApnsJsonContext.Default.ApnsPayload);
            using HttpResponseMessage
                response = await httpClient.PostAsync($"/3/device/{job.ApnsDeviceToken}", content);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Failed to send apns push notification. Status code: {Code}. Reason: {Body}",
                    response.StatusCode, responseBody);
            }
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("Push notification task cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send apns push notification");
        }
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