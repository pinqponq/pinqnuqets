namespace Pinqponq.RateLimiting;

/// <summary>Log event names this package puts on <c>Pinqponq.Contracts.IRequestErrorFeature</c>.</summary>
public static class RateLimitingEvents
{
    /// <summary>A rate limit rejected the request.</summary>
    public const string RequestRateLimited = "request_rate_limited";
}
