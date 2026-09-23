using BackingTrack.Generation.Jobs;

namespace BackingTrack.Generation.Notifications;

internal interface IPushNotificationEmitter
{
    Task SendApnsPushNotificationAsync(GenerationJob job);
}