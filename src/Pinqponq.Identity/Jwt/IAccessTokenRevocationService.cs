namespace Pinqponq.Identity.Jwt;

/// <summary>
/// Helper that parses an access token and records its <c>jti</c> in
/// <see cref="IAccessTokenRevocationStore"/> (e.g. logout).
/// </summary>
public interface IAccessTokenRevocationService
{
    /// <summary>
    /// Revokes the access token identified by its <c>jti</c> claim until its <c>exp</c>.
    /// No-ops when the token lacks <c>jti</c>/<c>exp</c> or fails basic parse.
    /// </summary>
    Task RevokeAccessTokenAsync(string token, CancellationToken cancellationToken = default);
}
