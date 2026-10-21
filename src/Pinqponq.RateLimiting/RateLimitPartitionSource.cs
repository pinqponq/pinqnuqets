namespace Pinqponq.RateLimiting;

/// <summary>What a rate limit policy counts requests by. Each distinct value gets its own budget.</summary>
public enum RateLimitPartitionSource
{
    /// <summary>
    /// The client address (<c>HttpContext.Connection.RemoteIpAddress</c>). Call
    /// <c>UseForwardedHeaders</c> before <c>UseRateLimiter</c> behind a proxy, otherwise every
    /// request counts against the proxy's address.
    /// </summary>
    ClientAddress,

    /// <summary>
    /// The authenticated user, read from the <see cref="RateLimitingOptions.UserIdClaimType"/>
    /// claim. A request without that claim is not limited by the policy, so use it only on
    /// endpoints that require authentication.
    /// </summary>
    User,

    /// <summary>The authenticated user when there is one, otherwise the client address.</summary>
    UserOrClientAddress,

    /// <summary>
    /// The value <see cref="RateLimitPolicyOptions.PartitionKeyResolver"/> returns, for example an
    /// API key header. When it returns no value, the client address is used.
    /// </summary>
    Custom,
}
