using System.Security.Claims;

namespace Pinqponq.Identity.Jwt;

/// <summary>
/// Issues signed JSON Web Tokens from a set of claims.
/// </summary>
public interface IJwtTokenGenerator
{
    /// <summary>
    /// Generates a signed JWT containing the supplied <paramref name="claims"/>. Issuer,
    /// audience and lifetime are taken from the configured <see cref="JwtOptions"/>.
    /// </summary>
    /// <param name="claims">Claims to embed in the token payload.</param>
    /// <param name="issuedAt">
    /// Optional issue time; defaults to the current UTC time. The token's
    /// <c>nbf</c> is set to this value and <c>exp</c> to this value plus the configured lifetime.
    /// </param>
    /// <returns>The compact serialized JWT string.</returns>
    string GenerateToken(IEnumerable<Claim> claims, DateTimeOffset? issuedAt = null);
}
