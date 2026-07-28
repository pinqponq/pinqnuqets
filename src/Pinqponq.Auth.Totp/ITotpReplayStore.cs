namespace Pinqponq.Auth.Totp;

/// <summary>
/// Persistence for accepted TOTP counters (replay protection). Implementations are supplied
/// by the consuming application (Redis, EF Core, …); this package ships no concrete store.
/// </summary>
public interface ITotpReplayStore
{
    /// <summary>
    /// Atomically accepts <paramref name="counter"/> for <paramref name="subjectKey"/>.
    /// Returns <see langword="false"/> when that counter was already accepted (replay).
    /// </summary>
    /// <param name="subjectKey">Stable per-user (or per-credential) key.</param>
    /// <param name="counter">The matched TOTP time-step counter.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<bool> TryAcceptAsync(
        string subjectKey,
        long counter,
        CancellationToken cancellationToken = default);
}
