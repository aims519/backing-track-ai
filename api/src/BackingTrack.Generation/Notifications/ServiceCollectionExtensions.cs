using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

using BackingTrack.Core.Config;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BackingTrack.Generation.Notifications;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPushNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        ApnsSettings? apnsSettings = configuration.GetSection("Apns").Get<ApnsSettings>();
        if (!IsApnsConfigUsable(apnsSettings))
        {
            services.AddScoped<IPushNotificationEmitter, NoOpPushNotificationEmitter>();
            return services;
        }

        services.AddHttpClient<IPushNotificationEmitter, PushNotificationEmitter>(options =>
        {
            options.BaseAddress = new(apnsSettings.ServerUrl);
            options.DefaultRequestHeaders.Add("apns-topic", apnsSettings.BundleId);
            options.DefaultRequestHeaders.Add("apns-priority", 10.ToString()); // highest priority
            options.DefaultRequestHeaders.Add("apns-push-type", "alert");
            options.Timeout = TimeSpan.FromSeconds(10);
        })
        .RemoveAllLoggers();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ApnsTokenProvider>();

        services.Configure<ApnsSettings>(configuration.GetSection("Apns"));

        return services;
    }

    private static bool IsApnsConfigUsable([NotNullWhen(true)] ApnsSettings? apnsSettings)
    {
        if (apnsSettings is null)
            return false;

        if (string.IsNullOrWhiteSpace(apnsSettings.TeamId)
            || string.IsNullOrWhiteSpace(apnsSettings.P8KeyId)
            || string.IsNullOrWhiteSpace(apnsSettings.P8Key)
            || string.IsNullOrWhiteSpace(apnsSettings.ServerUrl)
            || string.IsNullOrWhiteSpace(apnsSettings.BundleId))
            return false;

        try
        {
            using ECDsa key = ECDsa.Create();
            key.ImportFromPem(apnsSettings.P8Key);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return false;
        }
    }
}