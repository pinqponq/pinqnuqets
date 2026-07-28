namespace Pinqponq.Identity.Jwt;

/// <summary>
/// Persistence for revoked access-token JTIs until their natural expiry. Implementations
/// are supplied by the consuming application; this package ships no concrete store.
/// </summary>
public interface IAccessTokenRevocationStore
{
    /// <summary>Marks <paramref name="jti"/> as revoked until <paramref name="expiresAt"/>.</summary>
    Task RevokeAsync(
        string jti,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>Returns <see langword="true"/> when <paramref name="jti"/> is currently revoked.</summary>
    Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default);
}
