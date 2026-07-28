using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Pinqponq.Cache;

/// <summary>
/// Default <see cref="IDistributedLock"/> using Redis <c>LockTake</c>/<c>LockRelease</c>
/// with a per-acquisition token so only the owner can release. Optionally issues a fencing
/// token (atomic SET NX + INCR) and runs a renew watchdog.
/// </summary>
public sealed class RedisDistributedLock : IDistributedLock
{
    /// <summary>
    /// Atomically: SET lock NX PX, then INCR fence. Returns fence value or nil if lock not taken.
    /// KEYS[1]=lock KEYS[2]=fence ARGV[1]=token ARGV[2]=expiryMs
    /// </summary>
    private const string AcquireWithFenceScript = """
        if redis.call('set', KEYS[1], ARGV[1], 'NX', 'PX', ARGV[2]) then
          return redis.call('incr', KEYS[2])
        else
          return nil
        end
        """;

    private readonly IConnectionMultiplexer _connection;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisDistributedLock> _logger;

    /// <summary>Creates the lock provider over a shared connection multiplexer.</summary>
    public RedisDistributedLock(
        IConnectionMultiplexer connection,
        IOptions<RedisOptions> options,
        ILogger<RedisDistributedLock>? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? NullLogger<RedisDistributedLock>.Instance;
    }

    /// <inheritdoc />
    public Task<ILockHandle> AcquireAsync(
        string resource,
        TimeSpan expiry,
        CancellationToken cancellationToken = default) =>
        AcquireAsync(resource, expiry, options: null, cancellationToken);

    /// <inheritdoc />
    public async Task<ILockHandle> AcquireAsync(
        string resource,
        TimeSpan expiry,
        DistributedLockAcquireOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiry), "Lock expiry must be positive.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var database = _connection.GetDatabase();
        var key = (RedisKey)Prefixed(resource);
        var tokenValue = Guid.NewGuid().ToString("N");
        var token = (RedisValue)tokenValue;
        var issueFence = options?.IssueFencingToken ?? true;

        bool acquired;
        long? fencingToken = null;

        if (issueFence)
        {
            var fenceKey = (RedisKey)$"{Prefixed(resource)}:fence";
            var expiryMs = (long)Math.Ceiling(expiry.TotalMilliseconds);
            try
            {
                var result = await database.ScriptEvaluateAsync(
                    AcquireWithFenceScript,
                    [key, fenceKey],
                    [token, expiryMs]).ConfigureAwait(false);

                if (result.IsNull)
                {
                    acquired = false;
                }
                else
                {
                    acquired = true;
                    fencingToken = (long)result;
                }
            }
            catch (RedisException)
            {
                // Best-effort release if SET succeeded but script failed mid-flight (unlikely with Lua).
                try
                {
                    await database.LockReleaseAsync(key, token).ConfigureAwait(false);
                }
                catch (RedisException)
                {
                    // ignore
                }

                throw;
            }
        }
        else
        {
            try
            {
                acquired = await database.LockTakeAsync(key, token, expiry).ConfigureAwait(false);
            }
            catch (RedisException)
            {
                try
                {
                    await database.LockReleaseAsync(key, token).ConfigureAwait(false);
                }
                catch (RedisException)
                {
                    // ignore
                }

                throw;
            }
        }

        return new Handle(
            database,
            key,
            token,
            tokenValue,
            acquired,
            fencingToken,
            expiry,
            options?.RenewInterval,
            _logger);
    }

    private string Prefixed(string resource)
    {
        var prefix = string.IsNullOrEmpty(_options.InstanceName) ? string.Empty : _options.InstanceName;
        return $"{prefix}lock:{resource}";
    }

    private sealed class Handle : ILockHandle
    {
        private readonly IDatabase _database;
        private readonly RedisKey _key;
        private readonly RedisValue _token;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource? _watchdogCts;
        private readonly Task? _watchdogTask;
        private int _disposed;

        public Handle(
            IDatabase database,
            RedisKey key,
            RedisValue token,
            string tokenValue,
            bool acquired,
            long? fencingToken,
            TimeSpan expiry,
            TimeSpan? renewInterval,
            ILogger logger)
        {
            _database = database;
            _key = key;
            _token = token;
            _logger = logger;
            Acquired = acquired;
            Token = acquired ? tokenValue : null;
            FencingToken = fencingToken;

            if (acquired && renewInterval is { } interval && interval > TimeSpan.Zero)
            {
                _watchdogCts = new CancellationTokenSource();
                _watchdogTask = RunWatchdogAsync(expiry, interval, _watchdogCts.Token);
            }
        }

        public bool Acquired { get; }

        public string? Token { get; }

        public long? FencingToken { get; }

        public async Task<bool> TryExtendAsync(
            TimeSpan expiry,
            CancellationToken cancellationToken = default)
        {
            if (!Acquired || _disposed != 0)
            {
                return false;
            }

            if (expiry <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(expiry), "Lock expiry must be positive.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await _database.LockExtendAsync(_key, _token, expiry).ConfigureAwait(false);
            }
            catch (RedisException)
            {
                return false;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_watchdogCts is not null)
            {
                await _watchdogCts.CancelAsync().ConfigureAwait(false);
                try
                {
                    if (_watchdogTask is not null)
                    {
                        await _watchdogTask.ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on cancel.
                }

                _watchdogCts.Dispose();
            }

            if (!Acquired)
            {
                return;
            }

            try
            {
                await _database.LockReleaseAsync(_key, _token).ConfigureAwait(false);
            }
            catch (RedisException)
            {
                // Best-effort release: lock may already have expired or the connection dropped.
            }
        }

        private async Task RunWatchdogAsync(
            TimeSpan expiry,
            TimeSpan renewInterval,
            CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(renewInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    var extended = await TryExtendAsync(expiry, cancellationToken).ConfigureAwait(false);
                    if (!extended)
                    {
                        _logger.LogWarning(
                            "Distributed lock renew failed for key '{LockKey}'.",
                            _key.ToString());
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Dispose cancelled the watchdog.
            }
        }
    }
}
