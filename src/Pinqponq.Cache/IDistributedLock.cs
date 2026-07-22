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
}

/// <summary>
/// A held (or not-acquired) distributed lock. Disposing releases it if held.
/// </summary>
public interface ILockHandle : IAsyncDisposable
{
    /// <summary>Whether the lock was acquired.</summary>
    bool Acquired { get; }
}
