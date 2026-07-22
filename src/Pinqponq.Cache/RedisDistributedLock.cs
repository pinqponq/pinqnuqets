using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Pinqponq.Cache;

/// <summary>
/// Default <see cref="IDistributedLock"/> using Redis <c>LockTake</c>/<c>LockRelease</c>
/// with a per-acquisition token so only the owner can release.
/// </summary>
public sealed class RedisDistributedLock : IDistributedLock
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisOptions _options;

    /// <summary>Creates the lock provider over a shared connection multiplexer.</summary>
    public RedisDistributedLock(IConnectionMultiplexer connection, IOptions<RedisOptions> options)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ILockHandle> AcquireAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);

        var database = _connection.GetDatabase();
        var key = (RedisKey)Prefixed(resource);
        var token = (RedisValue)Guid.NewGuid().ToString("N");

        var acquired = await database.LockTakeAsync(key, token, expiry).ConfigureAwait(false);
        return new Handle(database, key, token, acquired);
    }

    private string Prefixed(string resource)
    {
        var prefix = string.IsNullOrEmpty(_options.InstanceName) ? string.Empty : _options.InstanceName;
        return $"{prefix}lock:{resource}";
    }

    private sealed class Handle(IDatabase database, RedisKey key, RedisValue token, bool acquired) : ILockHandle
    {
        public bool Acquired => acquired;

        public async ValueTask DisposeAsync()
        {
            if (acquired)
            {
                await database.LockReleaseAsync(key, token).ConfigureAwait(false);
            }
        }
    }
}
