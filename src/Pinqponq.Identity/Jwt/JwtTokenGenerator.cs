using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Pinqponq.Identity.Jwt;

/// <summary>
/// Default <see cref="IJwtTokenGenerator"/> built on
/// <see cref="JsonWebTokenHandler"/>.
/// </summary>
public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;
    private readonly JwtSigningKeyResolver _keyResolver;
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>Creates the generator from configured options and a key resolver.</summary>
    public JwtTokenGenerator(IOptions<JwtOptions> options, JwtSigningKeyResolver keyResolver)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _keyResolver = keyResolver ?? throw new ArgumentNullException(nameof(keyResolver));
    }

    /// <inheritdoc />
    public string GenerateToken(IEnumerable<Claim> claims, DateTimeOffset? issuedAt = null)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var now = issuedAt ?? DateTimeOffset.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.Add(_options.Lifetime).UtcDateTime,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = _keyResolver.CreateSigningCredentials(),
        };

        return _handler.CreateToken(descriptor);
    }
}
