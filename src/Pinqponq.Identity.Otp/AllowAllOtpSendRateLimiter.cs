namespace Pinqponq.Identity.Otp;

/// <summary>No-op rate limiter that always allows sends.</summary>
public sealed class AllowAllOtpSendRateLimiter : IOtpSendRateLimiter
{
    /// <inheritdoc />
    public Task<bool> TryAcquireAsync(
        string key,
        TimeSpan minInterval,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
