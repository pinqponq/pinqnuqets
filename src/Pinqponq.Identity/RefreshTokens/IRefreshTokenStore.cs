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

    /// <summary>Persists changes to an existing token (e.g. revocation, rotation link).</summary>
    Task UpdateAsync(RefreshToken token, CancellationToken cancellationToken = default);
}
