using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using BackingTrack.Core.Config;

using Microsoft.Extensions.Options;

namespace BackingTrack.Generation.Notifications;

internal class ApnsTokenProvider(IOptions<ApnsSettings> settings, TimeProvider timeProvider) : IDisposable
{
    private readonly ECDsa _key = CreateKey(settings.Value.P8Key);
    private readonly Lock _tokenLock = new();
    private string? _currentToken;
    private DateTimeOffset _tokenLastIssuedAt;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(50); // APNs rejects tokens older than 60 min and refreshes more often than every 20 min.

    public string GetToken()
    {
        lock (_tokenLock)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (_currentToken is null || (now - _tokenLastIssuedAt) > TokenLifetime)
            {
                _tokenLastIssuedAt = now;
                _currentToken = CreateToken(now.ToUnixTimeSeconds());
            }
            return _currentToken;
        }
    }

    private static ECDsa CreateKey(string pem)
    {
        ECDsa key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }

    private string CreateToken(long issuedAt)
    {
        string header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new ApnsJwtHeader("ES256", settings.Value.P8KeyId), ApnsJsonContext.Default.ApnsJwtHeader));
        string claims = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new ApnsJwtClaims(settings.Value.TeamId, issuedAt), ApnsJsonContext.Default.ApnsJwtClaims));

        string signingInput = $"{header}.{claims}";

        byte[] signature = _key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256);

        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    public void Dispose()
    {
        _key.Dispose();
    }
}

internal record ApnsJwtHeader(string Alg, string Kid);
internal record ApnsJwtClaims(string Iss, long Iat);