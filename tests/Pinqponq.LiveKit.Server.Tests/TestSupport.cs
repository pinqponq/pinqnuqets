using Pinqponq.LiveKit.Server.Auth;
using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pinqponq.LiveKit.Server.Tests;

internal static class TestCredentials
{
    public const string API_KEY = "APItestkey";
    public const string API_SECRET = "test-secret-with-enough-length-for-hs256";

    public static readonly LiveKitCredentials Credentials = new(API_KEY, API_SECRET);
    public static readonly StaticLiveKitCredentialsProvider Provider = new(Credentials);
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow() => _now;
}

internal static class JwtPayload
{
    public static JsonElement Decode(string token)
    {
        var payloadSegment = token.Split('.')[1];
        return JsonDocument.Parse(Base64Url.DecodeFromChars(payloadSegment)).RootElement.Clone();
    }
}

internal static class WebhookSigner
{
    public static string Sign(string body, LiveKitCredentials credentials, DateTimeOffset now)
    {
        var checksum = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var issuedAt = now.ToUnixTimeSeconds();
        var claims = new AccessTokenClaims
        {
            Issuer = credentials.ApiKey,
            NotBefore = issuedAt,
            ExpiresAt = now.AddMinutes(5).ToUnixTimeSeconds(),
            Sha256 = checksum
        };
        return JsonWebToken.Sign(claims, credentials.ApiSecret);
    }
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _responseBody;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
    {
        _statusCode = statusCode;
        _responseBody = responseBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(_statusCode) { Content = new StringContent(_responseBody, Encoding.UTF8, "application/json") };
    }
}
