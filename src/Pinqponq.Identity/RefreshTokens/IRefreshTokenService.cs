namespace Pinqponq.Identity.RefreshTokens;

/// <summary>
/// Issues, rotates and revokes refresh tokens. The service owns the token generation
/// and lifecycle rules; persistence is delegated to <see cref="IRefreshTokenStore"/>.
/// </summary>
public interface IRefreshTokenService
{
    /// <summary>Issues a brand new refresh token for the given subject.</summary>
    /// <param name="subject">The subject (typically a user id) to issue for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<RefreshTokenResult> IssueAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates a presented token: validates it, revokes it, links it to a freshly issued
    /// replacement and returns the replacement.
    /// </summary>
    /// <param name="presentedToken">The raw token value presented by the client.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InvalidRefreshTokenException">The token is unknown, expired or revoked.</exception>
    Task<RefreshTokenResult> RotateAsync(string presentedToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes a presented token so it can no longer be rotated.</summary>
    /// <param name="presentedToken">The raw token value presented by the client.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <exception cref="InvalidRefreshTokenException">The token is unknown, expired or revoked.</exception>
    Task RevokeAsync(string presentedToken, CancellationToken cancellationToken = default);
}
