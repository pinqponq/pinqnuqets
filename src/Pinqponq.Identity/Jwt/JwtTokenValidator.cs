using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Pinqponq.Identity.Jwt;

/// <summary>
/// Default <see cref="IJwtTokenValidator"/> built on
/// <see cref="JsonWebTokenHandler"/>.
/// </summary>
public sealed class JwtTokenValidator : IJwtTokenValidator
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly TokenValidationParameters _parameters;

    /// <summary>Creates the validator from configured options and a key resolver.</summary>
    public JwtTokenValidator(IOptions<JwtOptions> options, JwtSigningKeyResolver keyResolver)
    {
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
    public async Task<ClaimsPrincipal?> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = await _handler.ValidateTokenAsync(token, _parameters).ConfigureAwait(false);
        if (!result.IsValid || result.ClaimsIdentity is null)
        {
            return null;
        }

        return new ClaimsPrincipal(result.ClaimsIdentity);
    }
}
