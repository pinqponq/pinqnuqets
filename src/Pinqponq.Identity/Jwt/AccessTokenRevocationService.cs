using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Pinqponq.Identity.Jwt;

/// <summary>Default <see cref="IAccessTokenRevocationService"/>.</summary>
public sealed class AccessTokenRevocationService : IAccessTokenRevocationService
{
    private readonly IAccessTokenRevocationStore _store;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _parameters;

    /// <summary>
    /// Creates the service from a revocation store and the same signing material used
    /// for access tokens. Signature validation runs without consulting the revocation store
    /// (avoids circular DI with <see cref="JwtTokenValidator"/>).
    /// </summary>
    public AccessTokenRevocationService(
        IAccessTokenRevocationStore store,
        IOptions<JwtOptions> options,
        JwtSigningKeyResolver keyResolver)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(keyResolver);

        var o = options.Value;
        _parameters = new TokenValidationParameters
        {
            ValidateIssuer = o.ValidateIssuer,
            ValidIssuer = o.Issuer,
            ValidateAudience = o.ValidateAudience,
            ValidAudience = o.Audience,
            ValidateLifetime = o.ValidateLifetime,
            ClockSkew = o.ClockSkew,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = keyResolver.CreateValidationKey(),
            ValidAlgorithms = [keyResolver.Algorithm],
        };
    }

    /// <inheritdoc />
    public async Task RevokeAccessTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = await _handler.ValidateTokenAsync(token, _parameters).ConfigureAwait(false);
        if (!result.IsValid || result.ClaimsIdentity is null)
        {
            return;
        }

        var jti = result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
            ?? result.ClaimsIdentity.FindFirst("jti")?.Value;
        if (string.IsNullOrEmpty(jti))
        {
            return;
        }

        DateTimeOffset expiresAt;
        if (result.SecurityToken is JsonWebToken jwt && jwt.ValidTo != DateTime.MinValue)
        {
            expiresAt = new DateTimeOffset(DateTime.SpecifyKind(jwt.ValidTo, DateTimeKind.Utc));
        }
        else
        {
            expiresAt = DateTimeOffset.UtcNow;
        }

        await _store.RevokeAsync(jti, expiresAt, cancellationToken).ConfigureAwait(false);
    }
}
