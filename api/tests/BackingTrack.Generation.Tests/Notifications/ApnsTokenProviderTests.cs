using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using BackingTrack.Generation.Notifications;

using Microsoft.Extensions.Time.Testing;

namespace BackingTrack.Generation.Tests.Notifications;

public sealed class ApnsTokenProviderTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly FakeTimeProvider _time = new(Start);
    private readonly ApnsTokenProvider _provider;

    public ApnsTokenProviderTests()
    {
        _provider = ApnsTestFixtures.CreateTokenProvider(_key, _time);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _key.Dispose();
    }

    private static JsonElement DecodeSegment(string segment) =>
        JsonDocument.Parse(Base64Url.DecodeFromChars(segment)).RootElement;

    [Fact]
    public void GetToken_ReturnsHeaderClaimsAndSignatureSegments()
    {
        string[] parts = _provider.GetToken().Split('.');

        Assert.Equal(3, parts.Length);
        Assert.All(parts, p => Assert.False(string.IsNullOrEmpty(p)));
    }

    [Fact]
    public void GetToken_HeaderNamesES256AndTheKeyId()
    {
        JsonElement header = DecodeSegment(_provider.GetToken().Split('.')[0]);

        Assert.Equal("ES256", header.GetProperty("alg").GetString());
        Assert.Equal(ApnsTestFixtures.KeyId, header.GetProperty("kid").GetString());
    }

    [Fact]
    public void GetToken_ClaimsCarryTeamIdAndIssuedAtInUnixSeconds()
    {
        JsonElement claims = DecodeSegment(_provider.GetToken().Split('.')[1]);

        Assert.Equal(ApnsTestFixtures.TeamId, claims.GetProperty("iss").GetString());
        Assert.Equal(Start.ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
    }

    [Fact]
    public void GetToken_SignatureVerifiesAgainstThePublicKey()
    {
        string[] parts = _provider.GetToken().Split('.');
        byte[] signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        byte[] signature = Base64Url.DecodeFromChars(parts[2]);

        Assert.True(_key.VerifyData(signingInput, signature, HashAlgorithmName.SHA256));
    }

    [Fact]
    public void GetToken_SignatureIsRawRAndSNotDer()
    {
        // JWS ES256 wants the fixed 64-byte r||s form; a DER-encoded signature is ~70-72 bytes and
        // APNs rejects it as InvalidProviderToken with no further detail.
        byte[] signature = Base64Url.DecodeFromChars(_provider.GetToken().Split('.')[2]);

        Assert.Equal(64, signature.Length);
    }

    [Fact]
    public void GetToken_WithinTokenLifetime_ReturnsTheCachedToken()
    {
        string first = _provider.GetToken();

        _time.Advance(TimeSpan.FromMinutes(49));

        Assert.Equal(first, _provider.GetToken());
    }

    [Fact]
    public void GetToken_AfterTokenLifetime_IssuesANewTokenWithAFreshIssuedAt()
    {
        string first = _provider.GetToken();

        _time.Advance(TimeSpan.FromMinutes(51));
        string second = _provider.GetToken();

        Assert.NotEqual(first, second);
        JsonElement claims = DecodeSegment(second.Split('.')[1]);
        Assert.Equal(_time.GetUtcNow().ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
    }

    [Fact]
    public void GetToken_AtExactlyTokenLifetime_IssuesANewTokenWithAFreshIssuedAt()
    {
        string first = _provider.GetToken();

        _time.Advance(TimeSpan.FromMinutes(50));
        string second = _provider.GetToken();

        Assert.NotEqual(first, second);
        JsonElement claims = DecodeSegment(second.Split('.')[1]);
        Assert.Equal(_time.GetUtcNow().ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
    }
}