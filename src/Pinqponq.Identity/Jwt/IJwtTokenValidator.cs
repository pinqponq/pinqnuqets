using System.Security.Claims;

namespace Pinqponq.Identity.Jwt;

/// <summary>
/// Validates JSON Web Tokens produced with the configured signing key.
/// </summary>
public interface IJwtTokenValidator
{
    /// <summary>
    /// Validates <paramref name="token"/> against the configured issuer, audience,
    /// lifetime and signing key.
    /// </summary>
    /// <param name="token">The compact serialized JWT to validate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The authenticated <see cref="ClaimsPrincipal"/> when the token is valid;
    /// otherwise <see langword="null"/>.
    /// </returns>
    Task<ClaimsPrincipal?> ValidateAsync(string token, CancellationToken cancellationToken = default);
}
