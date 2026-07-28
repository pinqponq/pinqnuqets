namespace Pinqponq.Identity.RefreshTokens;

/// <summary>
/// Persistence abstraction for refresh tokens. Implementations are supplied by the
/// consuming application (EF Core, Dapper, Redis, …); this package deliberately ships
/// no concrete store so it stays free of storage dependencies.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>Persists a newly issued token.</summary>
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a token by its hash. Returns <see langword="null"/> when no record exists.
    /// </summary>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing token (e.g. revocation outside rotate).</summary>
    Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically revokes the token identified by <paramref name="tokenHash"/> only when it is
    /// still active at <paramref name="revokedAt"/>. Returns <see langword="true"/> when the
    /// revoke succeeded; <see langword="false"/> when the token is missing, expired or already
    /// revoked. Implementations must make this compare-and-set safe under concurrency.
    /// </summary>
    Task<bool> TryRevokeActiveAsync(
        string tokenHash,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically persists <paramref name="replacement"/> and sets
    /// <see cref="RefreshToken.ReplacedByTokenHash"/> on the already-revoked token identified by
    /// <paramref name="revokedTokenHash"/>. Implementations must perform add+link in a single
    /// transaction / MULTI / critical section so a crash cannot leave a revoked ancestor without
    /// a replacement link (which would disable reuse detection).
    /// </summary>
    Task CompleteRotationAsync(
        string revokedTokenHash,
        RefreshToken replacement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every refresh token belonging to <paramref name="subject"/>. Used when
    /// reuse of a rotated token is detected so the entire replacement chain is invalidated.
    /// </summary>
    Task RevokeAllForSubjectAsync(string subject, CancellationToken cancellationToken = default);
}
