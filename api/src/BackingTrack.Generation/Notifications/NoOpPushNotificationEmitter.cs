using BackingTrack.Generation.Jobs;

using Microsoft.Extensions.Logging;

namespace BackingTrack.Generation.Notifications;

internal class NoOpPushNotificationEmitter(ILogger<NoOpPushNotificationEmitter> logger) : IPushNotificationEmitter
{
    public Task SendApnsPushNotificationAsync(GenerationJob job)
    {
        if (job.ApnsDeviceToken is not null)
            logger.LogWarning("Job {Id} requested a push notification but APNs is not configured", job.Id);
        return Task.CompletedTask;
    }
}