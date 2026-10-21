using System.Net;
using Pinqponq.RateLimiting.DependencyInjection;
using Xunit;

namespace Pinqponq.RateLimiting.Tests;

public sealed class GlobalLimitTests
{
    private static RateLimitPolicyOptions PerKeyLimit(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        PartitionBy = RateLimitPartitionSource.Custom,
        PartitionKeyResolver = RateLimitingHost.ReadPartitionValue,
        IsPartitionKeySecret = true,
    };

    private static RateLimitPolicyOptions PerAddressLimit(int permitLimit) => new()
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromMinutes(1),
        PartitionBy = RateLimitPartitionSource.ClientAddress,
    };

    [Fact]
    public async Task Global_limit_applies_to_endpoints_without_a_policy()
    {
        await using var host = await RateLimitingHost.Start(services =>
            services.AddPinqponqRateLimiting(options => options.GlobalLimits.Add(PerAddressLimit(permitLimit: 2))));

        var statusCodes = await host.SendMany(3, () => host.Send(RateLimitingHost.UnlimitedPath, clientAddress: "10.0.0.1"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statusCodes);
    }

    [Fact]
    public async Task Chained_global_limits_reject_when_the_per_key_limit_is_used_up()
    {
        await using var host = await RateLimitingHost.Start(services =>
            services.AddPinqponqRateLimiting(options =>
            {
                options.GlobalLimits.Add(PerKeyLimit(permitLimit: 2));
                options.GlobalLimits.Add(PerAddressLimit(permitLimit: 10));
            }));

        var statusCodes = await host.SendMany(3, () => host.Send(RateLimitingHost.UnlimitedPath, clientAddress: "10.0.0.1", partitionValue: "key-1"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statusCodes);
    }

    [Fact]
    public async Task Chained_global_limits_reject_when_the_per_address_limit_is_used_up_by_changing_keys()
    {
        await using var host = await RateLimitingHost.Start(services =>
            services.AddPinqponqRateLimiting(options =>
            {
                options.GlobalLimits.Add(PerKeyLimit(permitLimit: 2));
                options.GlobalLimits.Add(PerAddressLimit(permitLimit: 3));
            }));

        var statusCodes = new List<HttpStatusCode>();
        for (var keyIndex = 0; keyIndex < 4; keyIndex++)
        {
            using var response = await host.Send(RateLimitingHost.UnlimitedPath, clientAddress: "10.0.0.1", partitionValue: $"key-{keyIndex}");
            statusCodes.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statusCodes);
    }

    [Fact]
    public async Task Budget_comes_back_when_the_window_ends()
    {
        await using var host = await RateLimitingHost.Start(services =>
            services.AddPinqponqRateLimiting(options => options.GlobalLimits.Add(new RateLimitPolicyOptions
            {
                PermitLimit = 1,
                Window = TimeSpan.FromMilliseconds(300),
            })));

        var withinWindow = await host.SendMany(2, () => host.Send(RateLimitingHost.UnlimitedPath, clientAddress: "10.0.0.1"));
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        var nextWindow = await host.SendMany(1, () => host.Send(RateLimitingHost.UnlimitedPath, clientAddress: "10.0.0.1"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], withinWindow);
        Assert.Equal([HttpStatusCode.OK], nextWindow);
    }

    [Fact]
    public async Task Global_limit_and_a_named_policy_both_apply()
    {
        await using var host = await RateLimitingHost.Start(services =>
            services.AddPinqponqRateLimiting(options =>
            {
                options.GlobalLimits.Add(PerAddressLimit(permitLimit: 10));
                options.Policies.Add(RateLimitingHost.PolicyName, PerAddressLimit(permitLimit: 1));
            }));

        var policyRequests = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1"));
        var unlimitedRequests = await host.SendMany(2, () => host.Send(RateLimitingHost.UnlimitedPath, clientAddress: "10.0.0.1"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], policyRequests);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK], unlimitedRequests);
    }
}
