using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Pinqponq.RateLimiting.DependencyInjection;
using Xunit;

namespace Pinqponq.RateLimiting.Tests;

public sealed class RegistrationTests
{
    private static RateLimitPolicyOptions ValidPolicy() => new()
    {
        PermitLimit = 1,
        Window = TimeSpan.FromMinutes(1),
    };

    public static TheoryData<string, Action<RateLimitingOptions>> InvalidOptions => new()
    {
        { "PermitLimit of rate limit policy 'test-policy' must be greater than 0", options => options.Policies.Add("test-policy", new RateLimitPolicyOptions { Window = TimeSpan.FromMinutes(1) }) },
        { "PermitLimit of rate limit policy 'test-policy' must be greater than 0", options => options.Policies.Add("test-policy", new RateLimitPolicyOptions { PermitLimit = -1, Window = TimeSpan.FromMinutes(1) }) },
        { "Window of rate limit policy 'test-policy' must be greater than 0", options => options.Policies.Add("test-policy", new RateLimitPolicyOptions { PermitLimit = 1 }) },
        { "Window of rate limit global limit 0 must be greater than 0", options => options.GlobalLimits.Add(new RateLimitPolicyOptions { PermitLimit = 1, Window = TimeSpan.FromSeconds(-1) }) },
        { "PermitLimit of rate limit global limit 1 must be greater than 0", options => { options.GlobalLimits.Add(ValidPolicy()); options.GlobalLimits.Add(new RateLimitPolicyOptions { Window = TimeSpan.FromMinutes(1) }); } },
        { "PartitionKeyResolver of rate limit policy 'test-policy' is required when PartitionBy is Custom", options => options.Policies.Add("test-policy", new RateLimitPolicyOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1), PartitionBy = RateLimitPartitionSource.Custom }) },
        { "A rate limit policy name must not be empty", options => options.Policies.Add(" ", ValidPolicy()) },
        { "Rate limit global limit 0 has no settings", options => options.GlobalLimits.Add(null!) },
        { "UserIdClaimType is required", options => options.UserIdClaimType = "" },
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public async Task Invalid_options_fail_at_startup(string expectedFailure, Action<RateLimitingOptions> configure)
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            RateLimitingHost.Start(services => services.AddPinqponqRateLimiting(configure)));

        Assert.Contains(exception.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Policies_and_global_limits_are_bound_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Policies:test-policy:PermitLimit"] = "1",
                ["RateLimiting:Policies:test-policy:Window"] = "00:01:00",
                ["RateLimiting:Policies:test-policy:PartitionBy"] = "Custom",
                ["RateLimiting:GlobalLimits:0:PermitLimit"] = "3",
                ["RateLimiting:GlobalLimits:0:Window"] = "00:01:00",
            })
            .Build();

        await using var host = await RateLimitingHost.Start(services =>
            services.AddPinqponqRateLimiting(
                configuration.GetSection(RateLimitingOptions.SectionName),
                options => options.Policies["test-policy"].PartitionKeyResolver = RateLimitingHost.ReadPartitionValue));

        var policyRequests = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1", partitionValue: "key-1"));
        var unlimitedRequests = await host.SendMany(4, () => host.Send(RateLimitingHost.UnlimitedPath, clientAddress: "10.0.0.2"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], policyRequests);
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], unlimitedRequests);
    }

    [Fact]
    public async Task Configuration_without_a_callback_registers_the_bound_policies()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Policies:test-policy:PermitLimit"] = "1",
                ["RateLimiting:Policies:test-policy:Window"] = "00:01:00",
            })
            .Build();

        await using var host = await RateLimitingHost.Start(services =>
            services.AddPinqponqRateLimiting(configuration.GetSection(RateLimitingOptions.SectionName)));

        var policyRequests = await host.SendMany(2, () => host.Send(RateLimitingHost.PolicyPath, clientAddress: "10.0.0.1"));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.TooManyRequests], policyRequests);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddPinqponqRateLimiting(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => services.AddPinqponqRateLimiting((Action<RateLimitingOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddPinqponqRateLimiting(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => services.AddPinqponqRateLimiting((IConfiguration)null!));
    }
}
