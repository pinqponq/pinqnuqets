using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Pinqponq.RateLimiting;

/// <summary>Builds the ASP.NET Core <see cref="RateLimiterOptions"/> from <see cref="RateLimitingOptions"/>.</summary>
internal static class RateLimiterSetup
{
    public static void Apply(
        RateLimiterOptions rateLimiterOptions,
        RateLimitingOptions rateLimitingOptions,
        RateLimitPartitionKeyResolver partitionKeyResolver)
    {
        rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        rateLimiterOptions.OnRejected = RateLimitRejectionHandler.OnRejected;

        foreach (var (policyName, policy) in rateLimitingOptions.Policies)
        {
            rateLimiterOptions.AddPolicy(policyName, httpContext => CreatePartition(httpContext, policy, partitionKeyResolver));
        }

        var globalLimiters = rateLimitingOptions.GlobalLimits
            .Select(globalLimit => PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => CreatePartition(httpContext, globalLimit, partitionKeyResolver)))
            .ToArray();
        rateLimiterOptions.GlobalLimiter = globalLimiters.Length switch
        {
            0 => null,
            1 => globalLimiters[0],
            _ => PartitionedRateLimiter.CreateChained(globalLimiters),
        };
    }

    private static RateLimitPartition<string> CreatePartition(
        HttpContext httpContext,
        RateLimitPolicyOptions policy,
        RateLimitPartitionKeyResolver partitionKeyResolver)
    {
        var partitionKey = partitionKeyResolver.Resolve(httpContext, policy);
        if (partitionKey is null)
        {
            return RateLimitPartition.GetNoLimiter(string.Empty);
        }

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => CreateFixedWindowOptions(policy));
    }

    private static FixedWindowRateLimiterOptions CreateFixedWindowOptions(RateLimitPolicyOptions policy) => new()
    {
        PermitLimit = policy.PermitLimit,
        Window = policy.Window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}
