using Microsoft.AspNetCore.Http;

namespace Pinqponq.RateLimiting;

/// <summary>
/// One fixed-window limit: at most <see cref="PermitLimit"/> requests per <see cref="Window"/>
/// for each partition. A request over the limit is rejected at once; it is never queued.
/// </summary>
public sealed class RateLimitPolicyOptions
{
    /// <summary>Requests allowed per partition in one window. Required: startup fails when it is not greater than zero.</summary>
    public int PermitLimit { get; set; }

    /// <summary>Length of the window. Required: startup fails when it is not greater than zero.</summary>
    public TimeSpan Window { get; set; }

    /// <summary>What requests are counted by. Defaults to <see cref="RateLimitPartitionSource.ClientAddress"/>.</summary>
    public RateLimitPartitionSource PartitionBy { get; set; } = RateLimitPartitionSource.ClientAddress;

    /// <summary>
    /// Returns the partition value for <see cref="RateLimitPartitionSource.Custom"/>, for example the
    /// value of an API key header. Required for <see cref="RateLimitPartitionSource.Custom"/>; it
    /// cannot be set from configuration.
    /// </summary>
    public Func<HttpContext, string?>? PartitionKeyResolver { get; set; }

    /// <summary>
    /// Whether the <see cref="PartitionKeyResolver"/> value is a secret, such as an API key. A secret
    /// value is kept only as its SHA-256 digest, so the raw value is never held as a partition key.
    /// </summary>
    public bool IsPartitionKeySecret { get; set; }
}
