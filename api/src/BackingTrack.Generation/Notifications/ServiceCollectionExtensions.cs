using System.Net;

using BackingTrack.Core.Config;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Notifications;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPushNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        ApnsSettings? apnsSettings = configuration.GetSection("Apns").Get<ApnsSettings>();
        if (apnsSettings == null)
            return services;

        services.AddHttpClient<IPushNotificationEmitter, PushNotificationEmitter>(options =>
        {
            options.BaseAddress = new(apnsSettings.ServerUrl);
            options.DefaultRequestHeaders.Add("apns-topic", apnsSettings.BundleId);
            options.DefaultRequestHeaders.Add("apns-priority", 10.ToString()); // highest priority
            options.DefaultRequestHeaders.Add("apns-push-type", "alert");
            options.DefaultRequestVersion = HttpVersion.Version20; // Required by APNS
            options.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            options.Timeout = TimeSpan.FromSeconds(10);
        });

        return services;
    }
}