namespace Pinqponq.Identity.Otp;

/// <summary>
/// Throttles OTP send attempts per key. Implementations are supplied by the consuming
/// application (Redis, memory, …). The package registers a no-op default.
/// </summary>
public interface IOtpSendRateLimiter
{
    /// <summary>
    /// Returns <see langword="true"/> when a new send is allowed and the attempt is recorded.
    /// </summary>
    /// <param name="key">Typically the OTP store key for recipient+purpose.</param>
    /// <param name="minInterval">Minimum time between successful acquires for this key.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<bool> TryAcquireAsync(
        string key,
        TimeSpan minInterval,
        CancellationToken cancellationToken = default);
}
