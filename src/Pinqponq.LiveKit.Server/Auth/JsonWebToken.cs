using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pinqponq.LiveKit.Server.Auth;

/// <summary>
/// Minimal HS256 JWT implementation; LiveKit only issues and accepts HS256 tokens.
/// </summary>
internal static class JsonWebToken
{
    private const string SIGNING_ALGORITHM = "HS256";
    private const string ENCODED_HEADER_JSON = """{"alg":"HS256","typ":"JWT"}""";
    private const char SEGMENT_SEPARATOR = '.';
    private const int SEGMENT_COUNT = 3;

    // Same leeway LiveKit server applies when it validates exp/nbf.
    private static readonly TimeSpan ClockLeeway = TimeSpan.FromMinutes(1);

    private static readonly string EncodedHeader = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(ENCODED_HEADER_JSON));

    private static readonly JsonSerializerOptions ClaimSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    public static string Sign(AccessTokenClaims claims, string apiSecret)
    {
        var encodedPayload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(claims, ClaimSerializerOptions));
        var signingInput = $"{EncodedHeader}{SEGMENT_SEPARATOR}{encodedPayload}";
        var encodedSignature = Base64Url.EncodeToString(ComputeSignature(signingInput, apiSecret));
        return $"{signingInput}{SEGMENT_SEPARATOR}{encodedSignature}";
    }

    /// <summary>
    /// Verifies algorithm, signature, issuer and validity window; throws <see cref="InvalidAccessTokenException"/> otherwise.
    /// </summary>
    public static AccessTokenClaims Verify(string token, LiveKitCredentials credentials, DateTimeOffset now)
    {
        var segments = token.Split(SEGMENT_SEPARATOR);
        if (segments.Length != SEGMENT_COUNT)
        {
            throw new InvalidAccessTokenException("Token is not a JWT.");
        }

        ValidateAlgorithm(segments[0]);
        ValidateSignature(segments, credentials.ApiSecret);

        var claims = DecodeSegment<AccessTokenClaims>(segments[1]);
        ValidateIssuer(claims, credentials.ApiKey);
        ValidateLifetime(claims, now);
        return claims;
    }

    private static void ValidateAlgorithm(string encodedHeader)
    {
        var header = DecodeSegment<JsonWebTokenHeader>(encodedHeader);
        if (header.Algorithm != SIGNING_ALGORITHM)
        {
            throw new InvalidAccessTokenException($"Unsupported signing algorithm '{header.Algorithm}'.");
        }
    }

    private static void ValidateSignature(string[] segments, string apiSecret)
    {
        var signingInput = $"{segments[0]}{SEGMENT_SEPARATOR}{segments[1]}";
        var expectedSignature = ComputeSignature(signingInput, apiSecret);
        var actualSignature = DecodeBase64Url(segments[2]);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
        {
            throw new InvalidAccessTokenException("Token signature is invalid.");
        }
    }

    private static void ValidateIssuer(AccessTokenClaims claims, string apiKey)
    {
        if (claims.Issuer != apiKey)
        {
            throw new InvalidAccessTokenException($"Token was issued by '{claims.Issuer}', expected '{apiKey}'.");
        }
    }

    private static void ValidateLifetime(AccessTokenClaims claims, DateTimeOffset now)
    {
        if (claims.ExpiresAt is not { } expiresAt)
        {
            throw new InvalidAccessTokenException("Token has no expiration.");
        }

        var isExpired = now - ClockLeeway > DateTimeOffset.FromUnixTimeSeconds(expiresAt);
        if (isExpired)
        {
            throw new InvalidAccessTokenException("Token has expired.");
        }

        var isNotYetValid = claims.NotBefore is { } notBefore
            && now + ClockLeeway < DateTimeOffset.FromUnixTimeSeconds(notBefore);
        if (isNotYetValid)
        {
            throw new InvalidAccessTokenException("Token is not valid yet.");
        }
    }

    private static byte[] ComputeSignature(string signingInput, string apiSecret) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiSecret), Encoding.UTF8.GetBytes(signingInput));

    private static T DecodeSegment<T>(string encodedSegment)
    {
        var segmentBytes = DecodeBase64Url(encodedSegment);
        try
        {
            return JsonSerializer.Deserialize<T>(segmentBytes, ClaimSerializerOptions)
                ?? throw new InvalidAccessTokenException("Token segment is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidAccessTokenException("Token segment is not valid JSON.", exception);
        }
    }

    private static byte[] DecodeBase64Url(string encodedSegment)
    {
        try
        {
            return Base64Url.DecodeFromChars(encodedSegment);
        }
        catch (FormatException exception)
        {
            throw new InvalidAccessTokenException("Token segment is not valid base64url.", exception);
        }
    }

    private sealed class JsonWebTokenHeader
    {
        [JsonPropertyName("alg")]
        public string? Algorithm { get; init; }
    }
}
