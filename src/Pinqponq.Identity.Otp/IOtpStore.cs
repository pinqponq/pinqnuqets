namespace Pinqponq.Identity.Otp;

/// <summary>
/// Persistence abstraction for OTP records. Supplied by the consuming application
/// (Redis, EF Core, …); this package ships no concrete store, matching the
/// interface-only approach used for refresh tokens.
/// </summary>
public interface IOtpStore
{
    /// <summary>Saves (creating or replacing) the record for its key.</summary>
    Task SaveAsync(OtpRecord record, CancellationToken cancellationToken = default);

    /// <summary>Finds a record by key, or null when absent.</summary>
    Task<OtpRecord?> FindAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Persists changes to an existing record (e.g. incremented attempts).</summary>
    Task UpdateAsync(OtpRecord record, CancellationToken cancellationToken = default);

    /// <summary>Removes a record by key.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the record only when its current <see cref="OtpRecord.CodeHash"/> matches
    /// <paramref name="expectedCodeHash"/>. Used so a failed send cannot delete a newer code
    /// written by a concurrent generate. Returns <see langword="true"/> when removed.
    /// </summary>
    Task<bool> TryRemoveAsync(
        string key,
        string expectedCodeHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically verifies and consumes an OTP. Implementations must perform expiry,
    /// attempt-limit, hash match, remove-on-success and attempts-increment-on-mismatch
    /// in a single concurrency-safe critical section.
    /// </summary>
    /// <param name="key">Store key built by the OTP service.</param>
    /// <param name="codeHash">Expected code hash (already peppered by the service).</param>
    /// <param name="maxAttempts">Maximum allowed verification attempts.</param>
    /// <param name="utcNow">Current UTC time used for expiry checks.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<OtpVerifyStatus> TryConsumeAsync(
        string key,
        string codeHash,
        int maxAttempts,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);
}
