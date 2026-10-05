namespace BackingTrack.Generation.Tests.Notifications;

// MeterListener subscribes by meter name, so every test class that creates a PushNotificationMetrics
// (directly or through PushNotificationEmitter) shares this collection to keep measurements from leaking.
[CollectionDefinition(Name)]
public class PushNotificationMetricsCollection
{
    public const string Name = "PushNotificationMetrics";
}