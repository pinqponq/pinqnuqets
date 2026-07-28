using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Pinqponq.Cache;

/// <summary>
/// Default <see cref="ICacheService"/> backed by StackExchange.Redis.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _connection;
    private readonly RedisOptions _options;

    /// <summary>Creates the cache over a shared connection multiplexer.</summary>
    public RedisCacheService(IConnectionMultiplexer connection, IOptions<RedisOptions> options)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    private IDatabase Database => _connection.GetDatabase();

    /// <inheritdoc />
    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        var value = await Database.StringGetAsync(Prefixed(key)).ConfigureAwait(false);
        return value.IsNull ? null : value.ToString();
    }

    /// <inheritdoc />
    public Task SetStringAsync(
        string key,
        string value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        var ttl = expiry ?? _options.DefaultTtl;
        return ttl is TimeSpan duration
            ? Database.StringSetAsync(Prefixed(key), value, duration)
            : Database.StringSetAsync(Prefixed(key), value);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="default"/> when the key is missing or the stored payload
    /// cannot be deserialized as <typeparamref name="T"/>.
    /// </remarks>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var json = await GetStringAsync(key, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <inheritdoc />
    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var json = JsonSerializer.Serialize(value);
        return SetStringAsync(key, json, expiry, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        return Database.KeyDeleteAsync(Prefixed(key));
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        cancellationToken.ThrowIfCancellationRequested();
        return Database.KeyExistsAsync(Prefixed(key));
    }

    private string Prefixed(string key) =>
        string.IsNullOrEmpty(_options.InstanceName) ? key : _options.InstanceName + key;
}
