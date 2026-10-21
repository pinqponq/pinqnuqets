using System.Security.Claims;

namespace Pinqponq.RateLimiting;

/// <summary>Rate limit policies of an application. Validated at startup.</summary>
public sealed class RateLimitingOptions
{
    /// <summary>Default configuration section name.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Named policies, applied to an endpoint with <c>[EnableRateLimiting("name")]</c> or
    /// <c>RequireRateLimiting("name")</c>.
    /// </summary>
    public IDictionary<string, RateLimitPolicyOptions> Policies { get; } =
        new Dictionary<string, RateLimitPolicyOptions>(StringComparer.Ordinal);

    /// <summary>
    /// Limits applied to every request, before any named policy. When there are several, a request
    /// must be allowed by each of them, for example a limit per API key and a looser one per client
    /// address. Empty means no global limit.
    /// </summary>
    public IList<RateLimitPolicyOptions> GlobalLimits { get; } = [];

    /// <summary>
    /// Claim that identifies the user for <see cref="RateLimitPartitionSource.User"/> and
    /// <see cref="RateLimitPartitionSource.UserOrClientAddress"/>. Defaults to
    /// <see cref="ClaimTypes.NameIdentifier"/>, the claim the JWT bearer handler maps <c>sub</c> to.
    /// </summary>
    public string UserIdClaimType { get; set; } = ClaimTypes.NameIdentifier;
}
