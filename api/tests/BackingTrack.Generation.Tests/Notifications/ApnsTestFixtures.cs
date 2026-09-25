using System.Security.Cryptography;

using BackingTrack.Core.Config;
using BackingTrack.Generation.Notifications;

using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Tests.Notifications;

internal static class ApnsTestFixtures
{
    public const string KeyId = "TESTKEY123";
    public const string TeamId = "TESTTEAM12";

    public static ApnsSettings CreateSettings(ECDsa key) => new()
    {
        ServerUrl = "https://api.sandbox.push.apple.com",
        P8Key = key.ExportPkcs8PrivateKeyPem(),
        P8KeyId = KeyId,
        TeamId = TeamId,
        BundleId = "com.example.backingtrack"
    };

    public static ApnsTokenProvider CreateTokenProvider(ECDsa key, TimeProvider timeProvider) =>
        new(Options.Create(CreateSettings(key)), timeProvider);
}