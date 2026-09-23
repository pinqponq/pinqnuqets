using Pinqponq.LiveKit.Server.Auth;
using Pinqponq.LiveKit.Server.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace Pinqponq.LiveKit.Server.Webhooks;

/// <summary>
/// Verifies and parses LiveKit webhook requests (<c>application/webhook+json</c>).
/// </summary>
public sealed class WebhookReceiver
{
    private const string BEARER_PREFIX = "Bearer ";

    private readonly ILiveKitCredentialsProvider _credentialsProvider;
    private readonly TimeProvider _timeProvider;

    public WebhookReceiver(ILiveKitCredentialsProvider credentialsProvider, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(credentialsProvider);

        _credentialsProvider = credentialsProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <param name="rawBody">Request body exactly as received; the signature covers these bytes.</param>
    /// <param name="authorizationHeader">Value of the <c>Authorization</c> header, with or without the Bearer prefix.</param>
    /// <param name="cancellationToken">Cancels credential retrieval.</param>
    /// <exception cref="LiveKitWebhookValidationException">The request is not authentic.</exception>
    public async Task<WebhookEvent> Receive(string rawBody, string? authorizationHeader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rawBody);

        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            throw new LiveKitWebhookValidationException("Authorization header is missing.");
        }

        var credentials = await _credentialsProvider.GetCredentials(cancellationToken);
        var claims = VerifyToken(ExtractToken(authorizationHeader), credentials);
        ValidateBodyChecksum(rawBody, claims.Sha256);

        return ProtoJson.Deserialize<WebhookEvent>(rawBody);
    }

    private AccessTokenClaims VerifyToken(string token, LiveKitCredentials credentials)
    {
        try
        {
            return JsonWebToken.Verify(token, credentials, _timeProvider.GetUtcNow());
        }
        catch (InvalidAccessTokenException exception)
        {
            throw new LiveKitWebhookValidationException($"Webhook token is invalid: {exception.Message}", exception);
        }
    }

    private static string ExtractToken(string authorizationHeader)
    {
        var trimmedHeader = authorizationHeader.Trim();
        return trimmedHeader.StartsWith(BEARER_PREFIX, StringComparison.OrdinalIgnoreCase)
            ? trimmedHeader[BEARER_PREFIX.Length..].Trim()
            : trimmedHeader;
    }

    private static void ValidateBodyChecksum(string rawBody, string? expectedChecksum)
    {
        if (string.IsNullOrEmpty(expectedChecksum))
        {
            throw new LiveKitWebhookValidationException("Webhook token carries no body checksum.");
        }

        var actualChecksum = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody)));
        var isChecksumMatching = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actualChecksum),
            Encoding.UTF8.GetBytes(expectedChecksum));
        if (!isChecksumMatching)
        {
            throw new LiveKitWebhookValidationException("Webhook body checksum does not match its token.");
        }
    }
}
