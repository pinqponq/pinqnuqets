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
}
