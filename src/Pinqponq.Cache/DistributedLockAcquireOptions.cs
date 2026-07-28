namespace Pinqponq.Cache;

/// <summary>Optional knobs for <see cref="IDistributedLock"/> acquire.</summary>
public sealed class DistributedLockAcquireOptions
{
    /// <summary>
    /// When set, a background watchdog extends the lock every interval until dispose.
    /// <see langword="null"/> disables the watchdog (default).
    /// </summary>
    public TimeSpan? RenewInterval { get; set; }

    /// <summary>
    /// When <see langword="true"/> (default), issues a monotonically increasing fencing
    /// token via Redis <c>INCR</c> after a successful acquire. Consumers must enforce the
    /// fencing token on the resource side; this package only mint/stores it on the handle.
    /// </summary>
    public bool IssueFencingToken { get; set; } = true;
}
