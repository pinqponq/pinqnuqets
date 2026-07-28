namespace Pinqponq.Cache;

/// <summary>
/// A best-effort distributed lock backed by Redis.
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Attempts to acquire a lock on <paramref name="resource"/> for up to
    /// <paramref name="expiry"/>. Returns a handle whose <see cref="ILockHandle.Acquired"/>
    /// indicates success; dispose it to release.
    /// </summary>
    Task<ILockHandle> AcquireAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire a lock with optional fencing token and renew watchdog.
    /// </summary>
    Task<ILockHandle> AcquireAsync(
        string resource,
        TimeSpan expiry,
        DistributedLockAcquireOptions? options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A held (or not-acquired) distributed lock. Disposing releases it if held.
/// </summary>
public interface ILockHandle : IAsyncDisposable
{
    /// <summary>Whether the lock was acquired.</summary>
    bool Acquired { get; }

    /// <summary>Opaque ownership token used for Redis LockExtend/LockRelease; null when not acquired.</summary>
    string? Token { get; }

    /// <summary>
    /// Monotonic fencing token for this resource (when issued). Consumers should reject
    /// writes with a stale fencing token. Null when not acquired or fencing was disabled.
    /// </summary>
    long? FencingToken { get; }

    /// <summary>Extends the lock TTL if still held by this token.</summary>
    Task<bool> TryExtendAsync(TimeSpan expiry, CancellationToken cancellationToken = default);
}
