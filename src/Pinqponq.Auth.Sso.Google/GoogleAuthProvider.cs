using System.Security.Cryptography;
using System.Text;
using global::Google.Apis.Auth;
using Microsoft.Extensions.Options;
using Pinqponq.Auth.Sso.Abstractions;

namespace Pinqponq.Auth.Sso.Google;

/// <summary>
/// <see cref="IExternalAuthProvider"/> that validates Google id_tokens via
/// <see cref="GoogleJsonWebSignature"/>.
/// </summary>
public sealed class GoogleAuthProvider : IExternalAuthProvider
{
    /// <summary>The provider name handled by this implementation.</summary>
    public const string Name = "Google";

    private readonly GoogleAuthOptions _options;

    /// <summary>Creates the provider from configured options.</summary>
    public GoogleAuthProvider(IOptions<GoogleAuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public string ProviderName => Name;

    /// <inheritdoc />
    public async Task<ExternalAuthResult> AuthenticateAsync(
        ExternalAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(request.AuthorizationCode)
            && string.IsNullOrWhiteSpace(request.IdToken))
        {
            return ExternalAuthResult.Failure(
                "Google authorization-code exchange is not supported; present an id_token.");
        }

        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return ExternalAuthResult.Failure("An id_token is required for Google authentication.");
        }

        if (_options.ClientIds.Count == 0)
        {
            return ExternalAuthResult.Failure(
                "GoogleAuthOptions.ClientIds must contain at least one audience.");
        }

        if (_options.RequireNonce && string.IsNullOrEmpty(request.Nonce))
        {
            return ExternalAuthResult.Failure("Invalid Google id_token.");
        }

        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = _options.ClientIds,
        };

        if (!string.IsNullOrWhiteSpace(_options.HostedDomain))
        {
            settings.HostedDomain = _options.HostedDomain;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(request.Nonce)
                && !FixedTimeEquals(request.Nonce, payload.Nonce))
            {
                return ExternalAuthResult.Failure("Invalid Google id_token.");
            }

            if (_options.RequireEmailVerified && !payload.EmailVerified)
            {
                return ExternalAuthResult.Failure("Invalid Google id_token.");
            }

            var user = new ExternalUserInfo
            {
                Subject = payload.Subject,
                Provider = Name,
                Email = payload.Email,
                EmailVerified = payload.EmailVerified,
                Name = payload.Name,
                GivenName = payload.GivenName,
                FamilyName = payload.FamilyName,
                Picture = payload.Picture,
            };

            return ExternalAuthResult.Success(user);
        }
        catch (InvalidJwtException)
        {
            return ExternalAuthResult.Failure("Invalid Google id_token.");
        }
    }

    private static bool FixedTimeEquals(string expected, string? actual)
    {
        if (actual is null)
        {
            return false;
        }

        var a = Encoding.UTF8.GetBytes(expected);
        var b = Encoding.UTF8.GetBytes(actual);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}