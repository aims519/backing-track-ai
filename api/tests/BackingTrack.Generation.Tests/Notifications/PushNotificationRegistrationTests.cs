using System.Security.Cryptography;

using BackingTrack.Generation.Notifications;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackingTrack.Generation.Tests.Notifications;

public sealed class PushNotificationRegistrationTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose() => _key.Dispose();

    /// <summary>What appsettings.json ships with: the non-secret values only.</summary>
    private static Dictionary<string, string?> NonSecretSettings() => new()
    {
        ["Apns:ServerUrl"] = "https://api.sandbox.push.apple.com",
        ["Apns:BundleId"] = "com.example.backingtrack"
    };

    private Dictionary<string, string?> CompleteSettings()
    {
        Dictionary<string, string?> settings = NonSecretSettings();
        settings["Apns:P8Key"] = _key.ExportPkcs8PrivateKeyPem();
        settings["Apns:P8KeyId"] = ApnsTestFixtures.KeyId;
        settings["Apns:TeamId"] = ApnsTestFixtures.TeamId;
        return settings;
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddPushNotifications(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static IPushNotificationEmitter ResolveEmitter(ServiceProvider provider)
    {
        using IServiceScope scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPushNotificationEmitter>();
    }

    [Fact]
    public void AddPushNotifications_CompleteSettings_RegistersTheApnsEmitter()
    {
        using ServiceProvider provider = BuildProvider(CompleteSettings());

        Assert.IsType<PushNotificationEmitter>(ResolveEmitter(provider));
    }

    [Fact]
    public void AddPushNotifications_CompleteSettings_BindsTheTokenProviderToTheConfiguredKey()
    {
        using ServiceProvider provider = BuildProvider(CompleteSettings());

        // Resolving and signing proves the IOptions<ApnsSettings> binding and TimeProvider are both
        // wired, not just that the emitter type was registered.
        string token = provider.GetRequiredService<ApnsTokenProvider>().GetToken();

        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void AddPushNotifications_NoApnsSection_RegistersTheNoOpEmitter()
    {
        using ServiceProvider provider = BuildProvider([]);

        Assert.IsType<NoOpPushNotificationEmitter>(ResolveEmitter(provider));
    }

    [Fact]
    public void AddPushNotifications_OnlyNonSecretSettings_RegistersTheNoOpEmitter()
    {
        // The shape of a deploy where the secrets were never set - the case that used to take the
        // host down on the first job.
        using ServiceProvider provider = BuildProvider(NonSecretSettings());

        Assert.IsType<NoOpPushNotificationEmitter>(ResolveEmitter(provider));
    }

    [Theory]
    [InlineData("Apns:P8Key")]
    [InlineData("Apns:P8KeyId")]
    [InlineData("Apns:TeamId")]
    [InlineData("Apns:ServerUrl")]
    [InlineData("Apns:BundleId")]
    public void AddPushNotifications_ARequiredSettingIsBlank_RegistersTheNoOpEmitter(string key)
    {
        Dictionary<string, string?> settings = CompleteSettings();
        settings[key] = "   ";

        using ServiceProvider provider = BuildProvider(settings);

        Assert.IsType<NoOpPushNotificationEmitter>(ResolveEmitter(provider));
    }

    [Theory]
    [InlineData("FROM_USER_SECRETS")]
    [InlineData("-----BEGIN PRIVATE KEY-----\nnot-a-key\n-----END PRIVATE KEY-----")]
    public void AddPushNotifications_P8KeyIsNotALoadablePem_RegistersTheNoOpEmitter(string p8Key)
    {
        Dictionary<string, string?> settings = CompleteSettings();
        settings["Apns:P8Key"] = p8Key;

        using ServiceProvider provider = BuildProvider(settings);

        Assert.IsType<NoOpPushNotificationEmitter>(ResolveEmitter(provider));
    }

    [Fact]
    public void AddPushNotifications_TruncatedPem_RegistersTheNoOpEmitter()
    {
        Dictionary<string, string?> settings = CompleteSettings();
        string pem = settings["Apns:P8Key"]!;
        settings["Apns:P8Key"] = pem[..(pem.Length / 2)];

        using ServiceProvider provider = BuildProvider(settings);

        Assert.IsType<NoOpPushNotificationEmitter>(ResolveEmitter(provider));
    }

    [Fact]
    public async Task NoOpEmitter_JobWithDeviceToken_CompletesWithoutThrowing()
    {
        await using ServiceProvider provider = BuildProvider([]);
        IPushNotificationEmitter emitter = ResolveEmitter(provider);

        Exception? ex = await Record.ExceptionAsync(() => emitter.SendApnsPushNotificationAsync(
            Workflows.WorkflowTestFixtures.CreateGenerationJob() with { ApnsDeviceToken = new string('a', 64) }));

        Assert.Null(ex);
    }
}