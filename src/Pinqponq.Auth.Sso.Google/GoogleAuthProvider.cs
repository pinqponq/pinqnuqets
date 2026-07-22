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

        if (string.IsNullOrWhiteSpace(request.IdToken))
        {
            return ExternalAuthResult.Failure("An id_token is required for Google authentication.");
        }

        var settings = new GoogleJsonWebSignature.ValidationSettings();
        if (_options.ClientIds.Count > 0)
        {
            settings.Audience = _options.ClientIds;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings)
                .ConfigureAwait(false);

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
        catch (InvalidJwtException ex)
        {
            return ExternalAuthResult.Failure($"Invalid Google id_token: {ex.Message}");
        }
    }
}
