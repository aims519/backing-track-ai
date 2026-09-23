namespace BackingTrack.Generation.Jobs;

internal interface IPushNotificationEmitter
{
    Task SendApnsPushNotificationAsync(GenerationJob job);
}