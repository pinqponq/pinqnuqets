using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Pinqponq.RateLimiting;

/// <summary>
/// Turns a request into the key of its rate limit partition. Each source has its own prefix, so a
/// user id never shares a budget with an address or a custom value that has the same text.
/// </summary>
internal sealed class RateLimitPartitionKeyResolver
{
    public const string MissingClientAddressPartitionKey = "address:missing";

    private const string ClientAddressPrefix = "address:";
    private const string UserPrefix = "user:";
    private const string CustomPrefix = "custom:";
    private const string SecretCustomPrefix = "custom-sha256:";

    private readonly string _userIdClaimType;
    private readonly ILogger<RateLimitPartitionKeyResolver> _logger;
    private int _isMissingClientAddressLogged;

    public RateLimitPartitionKeyResolver(string userIdClaimType, ILogger<RateLimitPartitionKeyResolver> logger)
    {
        _userIdClaimType = userIdClaimType;
        _logger = logger;
    }

    /// <summary>Returns <see langword="null"/> when the request is not limited by the policy.</summary>
    public string? Resolve(HttpContext context, RateLimitPolicyOptions policy) => policy.PartitionBy switch
    {
        RateLimitPartitionSource.ClientAddress => ResolveClientAddress(context),
        RateLimitPartitionSource.User => ResolveUser(context),
        RateLimitPartitionSource.UserOrClientAddress => ResolveUser(context) ?? ResolveClientAddress(context),
        RateLimitPartitionSource.Custom => ResolveCustom(context, policy) ?? ResolveClientAddress(context),
        _ => throw new InvalidOperationException($"Unknown rate limit partition source '{policy.PartitionBy}'."),
    };

    private string ResolveClientAddress(HttpContext context)
    {
        var clientAddress = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(clientAddress))
        {
            return ClientAddressPrefix + clientAddress;
        }

        if (IsFirstMissingClientAddress())
        {
            _logger.LogWarning(
                "A request has no client address; every request without one shares the {PartitionKey} rate limit partition. Call UseForwardedHeaders before UseRateLimiter when the API runs behind a proxy.",
                MissingClientAddressPartitionKey);
        }

        return MissingClientAddressPartitionKey;
    }

    private string? ResolveUser(HttpContext context)
    {
        var userId = context.User.FindFirst(_userIdClaimType)?.Value;
        return string.IsNullOrWhiteSpace(userId) ? null : UserPrefix + userId;
    }

    private static string? ResolveCustom(HttpContext context, RateLimitPolicyOptions policy)
    {
        var partitionValue = policy.PartitionKeyResolver?.Invoke(context);
        if (string.IsNullOrWhiteSpace(partitionValue))
        {
            return null;
        }

        return policy.IsPartitionKeySecret
            ? SecretCustomPrefix + HashSecret(partitionValue)
            : CustomPrefix + partitionValue;
    }

    private static string HashSecret(string secretValue) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secretValue)));

    private bool IsFirstMissingClientAddress() => Interlocked.CompareExchange(ref _isMissingClientAddressLogged, 1, 0) == 0;
}
