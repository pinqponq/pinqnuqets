using System.Net;
using Pinqponq.RateLimiting.DependencyInjection;
using Xunit;

namespace Pinqponq.RateLimiting.Tests;

public sealed class PartitionTests
{
    private static readonly List<HttpStatusCode> AllowedThenRejected = [HttpStatusCode.OK, HttpStatusCode.TooManyRequests];

    private static RateLimitPolicyOptions OneRequestPerMinute(RateLimitPartitionSource partitionBy, bool isPartitionKeySecret = false) => new()
    {
        PermitLimit = 1,
        Window = TimeSpan.FromMinutes(1),
        PartitionBy = partitionBy,
        PartitionKeyResolver = partitionBy == RateLimitPartitionSource.Custom ? RateLimitingHost.ReadPartitionValue : null,
        IsPartitionKeySecret = isPartitionKeySecret,
    };

    [Fact]
    public async Task Client_addresses_do_not_share_a_budget()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(OneRequestPerMinute(RateLimitPartitionSource.ClientAddress));

        var firstAddress = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1"));
        var secondAddress = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.2"));

        Assert.Equal(AllowedThenRejected, firstAddress);
        Assert.Equal(AllowedThenRejected, secondAddress);
    }

    [Fact]
    public async Task Users_do_not_share_a_budget_even_from_the_same_address()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(OneRequestPerMinute(RateLimitPartitionSource.User));

        var firstUser = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1", userId: "user-1"));
        var secondUser = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1", userId: "user-2"));

        Assert.Equal(AllowedThenRejected, firstUser);
        Assert.Equal(AllowedThenRejected, secondUser);
    }

    [Fact]
    public async Task User_policy_does_not_limit_a_request_without_a_user()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(OneRequestPerMinute(RateLimitPartitionSource.User));

        var anonymousRequests = await host.SendMany(3, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1"));

        Assert.All(anonymousRequests, statusCode => Assert.Equal(HttpStatusCode.OK, statusCode));
    }

    [Fact]
    public async Task User_is_read_from_the_configured_claim_type()
    {
        await using var host = await RateLimitingHost.Start(
            services => services.AddPinqponqRateLimiting(options =>
            {
                options.UserIdClaimType = "custom-user-id";
                options.Policies.Add(RateLimitingHost.PolicyName, OneRequestPerMinute(RateLimitPartitionSource.User));
            }),
            userIdClaimType: "custom-user-id");

        var userRequests = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, userId: "user-1"));

        Assert.Equal(AllowedThenRejected, userRequests);
    }

    [Fact]
    public async Task User_or_client_address_counts_a_user_apart_from_anonymous_requests_of_its_address()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(OneRequestPerMinute(RateLimitPartitionSource.UserOrClientAddress));

        var anonymousRequests = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1"));
        var userRequests = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1", userId: "user-1"));

        Assert.Equal(AllowedThenRejected, anonymousRequests);
        Assert.Equal(AllowedThenRejected, userRequests);
    }

    [Fact]
    public async Task User_id_equal_to_an_address_does_not_share_the_budget_of_that_address()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(OneRequestPerMinute(RateLimitPartitionSource.UserOrClientAddress));

        var addressRequests = await host.SendMany(1, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1"));
        var userRequests = await host.SendMany(1, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.2", userId: "10.0.0.1"));

        Assert.Equal([HttpStatusCode.OK], addressRequests);
        Assert.Equal([HttpStatusCode.OK], userRequests);
    }

    [Fact]
    public async Task Custom_values_do_not_share_a_budget()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(OneRequestPerMinute(RateLimitPartitionSource.Custom));

        var firstKey = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1", partitionValue: "key-1"));
        var secondKey = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1", partitionValue: "key-2"));

        Assert.Equal(AllowedThenRejected, firstKey);
        Assert.Equal(AllowedThenRejected, secondKey);
    }

    [Fact]
    public async Task Custom_policy_without_a_value_counts_by_client_address()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(OneRequestPerMinute(RateLimitPartitionSource.Custom));

        var withoutValue = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1"));
        var otherAddress = await host.SendMany(1, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.2"));

        Assert.Equal(AllowedThenRejected, withoutValue);
        Assert.Equal([HttpStatusCode.OK], otherAddress);
    }

    [Fact]
    public async Task Secret_custom_values_still_get_their_own_budget()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(OneRequestPerMinute(RateLimitPartitionSource.Custom, isPartitionKeySecret: true));

        var firstKey = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1", partitionValue: "secret-1"));
        var secondKey = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1", partitionValue: "secret-2"));

        Assert.Equal(AllowedThenRejected, firstKey);
        Assert.Equal(AllowedThenRejected, secondKey);
    }

    [Fact]
    public async Task Policy_does_not_limit_an_endpoint_without_it()
    {
        await using var host = await RateLimitingHost.StartWithPolicy(OneRequestPerMinute(RateLimitPartitionSource.ClientAddress));

        var unlimitedRequests = await host.SendMany(3, () => host.Send(RateLimitingHost.UnlimitedPath, clientAddress: "10.0.0.1"));

        Assert.All(unlimitedRequests, statusCode => Assert.Equal(HttpStatusCode.OK, statusCode));
    }
}
