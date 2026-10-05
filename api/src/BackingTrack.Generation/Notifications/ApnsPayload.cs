namespace BackingTrack.Generation.Notifications;

internal record ApnsPayload(ApnsAps Aps, Guid JobId);
internal record ApnsAps(ApnsAlert Alert, string Sound = "default");
internal record ApnsAlert(string Title, string Body);
internal record ApnsRejection(string Reason);