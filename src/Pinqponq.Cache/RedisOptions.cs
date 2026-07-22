namespace Pinqponq.Cache;

/// <summary>
/// Configuration for the Redis cache.
/// </summary>
public sealed class RedisOptions
{
    /// <summary>
    /// StackExchange.Redis connection string
    /// (e.g. <c>host:6379,password=...,defaultDatabase=0,abortConnect=false</c>).
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Optional key prefix applied to every cache key (e.g. an app/instance name).</summary>
    public string? InstanceName { get; set; }

    /// <summary>Default expiry applied when a set call does not specify one. Null means no expiry.</summary>
    public TimeSpan? DefaultTtl { get; set; }
}
