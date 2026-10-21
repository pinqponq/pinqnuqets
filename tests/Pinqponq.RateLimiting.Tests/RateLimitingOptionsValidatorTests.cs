using Xunit;

namespace Pinqponq.RateLimiting.Tests;

public sealed class RateLimitingOptionsValidatorTests
{
    private static readonly RateLimitingOptionsValidator Validator = new();

    [Fact]
    public void Valid_options_succeed()
    {
        var options = new RateLimitingOptions();
        options.Policies.Add("test-policy", new RateLimitPolicyOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) });
        options.GlobalLimits.Add(new RateLimitPolicyOptions { PermitLimit = 1, Window = TimeSpan.FromMinutes(1) });

        var result = Validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Policy_with_an_empty_name_is_reported_once_without_checking_its_settings()
    {
        var options = new RateLimitingOptions();
        options.Policies.Add(" ", new RateLimitPolicyOptions());

        var result = Validator.Validate(name: null, options);

        Assert.True(result.Failed);
        Assert.Equal(["A rate limit policy name must not be empty."], result.Failures);
    }
}
