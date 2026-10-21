using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Pinqponq.RateLimiting.Tests;

public sealed class RateLimitPartitionKeyResolverTests
{
    private const string SecretValue = "live-key-123";

    private static readonly RateLimitPartitionKeyResolver Resolver =
        new(ClaimTypes.NameIdentifier, NullLogger<RateLimitPartitionKeyResolver>.Instance);

    private static DefaultHttpContext CreateContext(string? clientAddress = null, string? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = clientAddress is null ? null : IPAddress.Parse(clientAddress);
        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"));
        }

        return context;
    }

    private static RateLimitPolicyOptions CustomPolicy(string? partitionValue, bool isPartitionKeySecret) => new()
    {
        PermitLimit = 1,
        Window = TimeSpan.FromMinutes(1),
        PartitionBy = RateLimitPartitionSource.Custom,
        PartitionKeyResolver = _ => partitionValue,
        IsPartitionKeySecret = isPartitionKeySecret,
    };

    [Fact]
    public void Secret_custom_value_is_kept_only_as_its_sha256_digest()
    {
        var partitionKey = Resolver.Resolve(CreateContext("10.0.0.1"), CustomPolicy(SecretValue, isPartitionKeySecret: true));

        var expectedDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SecretValue)));
        Assert.Equal($"custom-sha256:{expectedDigest}", partitionKey);
        Assert.DoesNotContain(SecretValue, partitionKey, StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_value_that_is_not_a_secret_is_kept_as_it_is()
    {
        var partitionKey = Resolver.Resolve(CreateContext("10.0.0.1"), CustomPolicy("tenant-7", isPartitionKeySecret: false));

        Assert.Equal("custom:tenant-7", partitionKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Custom_policy_without_a_value_uses_the_client_address(string? partitionValue)
    {
        var partitionKey = Resolver.Resolve(CreateContext("10.0.0.1"), CustomPolicy(partitionValue, isPartitionKeySecret: true));

        Assert.Equal("address:10.0.0.1", partitionKey);
    }

    [Fact]
    public void Each_source_has_its_own_prefix()
    {
        var context = CreateContext("10.0.0.1", userId: "user-1");

        var addressKey = Resolver.Resolve(context, new RateLimitPolicyOptions { PartitionBy = RateLimitPartitionSource.ClientAddress });
        var userKey = Resolver.Resolve(context, new RateLimitPolicyOptions { PartitionBy = RateLimitPartitionSource.User });
        var userOrAddressKey = Resolver.Resolve(context, new RateLimitPolicyOptions { PartitionBy = RateLimitPartitionSource.UserOrClientAddress });

        Assert.Equal("address:10.0.0.1", addressKey);
        Assert.Equal("user:user-1", userKey);
        Assert.Equal("user:user-1", userOrAddressKey);
    }

    [Fact]
    public void User_source_without_a_user_has_no_partition()
    {
        var partitionKey = Resolver.Resolve(CreateContext("10.0.0.1"), new RateLimitPolicyOptions { PartitionBy = RateLimitPartitionSource.User });

        Assert.Null(partitionKey);
    }

    [Fact]
    public void Unknown_partition_source_throws()
    {
        var policy = new RateLimitPolicyOptions { PartitionBy = (RateLimitPartitionSource)99 };

        var exception = Assert.Throws<InvalidOperationException>(() => Resolver.Resolve(CreateContext("10.0.0.1"), policy));

        Assert.Equal("Unknown rate limit partition source '99'.", exception.Message);
    }
}
