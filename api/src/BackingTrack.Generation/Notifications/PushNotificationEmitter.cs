using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Jobs;

internal class PushNotificationEmitter(HttpClient httpClient, ILogger<PushNotificationEmitter> logger) : IPushNotificationEmitter
{
    public async Task SendApnsPushNotificationAsync(GenerationJob job)
    {
        if (job.ApnsDeviceToken is null)
        {
            logger.LogInformation("No ApnsDeviceToken found for the job, skipping send");
            return;
        }
        
        object message = new { job.Id, job.State, job.KeyRoot, job.KeyMode, job.Style };
        HttpContent content = new StringContent(JsonSerializer.Serialize(message));
        await httpClient.PostAsync($"/3/device/{job.ApnsDeviceToken}", content);
        
        throw new NotImplementedException();
    }
}