using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqponq.Hosting.Maintenance.DependencyInjection;
using Xunit;

namespace Pinqponq.Hosting.Maintenance.Tests;

public sealed class MaintenanceModeOptionsValidatorTests
{
    private readonly MaintenanceModeOptionsValidator _validator = new();

    [Fact]
    public void Default_options_are_valid()
    {
        var result = _validator.Validate(name: null, new MaintenanceModeOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(599)]
    public void Status_code_at_the_edges_of_the_range_is_valid(int statusCode)
    {
        var result = _validator.Validate(name: null, new MaintenanceModeOptions { StatusCode = statusCode });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(399)]
    [InlineData(600)]
    public void Status_code_outside_the_range_fails(int statusCode)
    {
        var result = _validator.Validate(name: null, new MaintenanceModeOptions { StatusCode = statusCode });

        Assert.Equal("StatusCode must be from 400 to 599.", Assert.Single(result.Failures!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_response_code_fails(string responseCode)
    {
        var result = _validator.Validate(name: null, new MaintenanceModeOptions { ResponseCode = responseCode });

        Assert.Equal("ResponseCode must not be blank.", Assert.Single(result.Failures!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_event_name_fails(string eventName)
    {
        var result = _validator.Validate(name: null, new MaintenanceModeOptions { EventName = eventName });

        Assert.Equal("EventName must not be blank.", Assert.Single(result.Failures!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Retry_after_of_zero_or_less_fails(int retryAfterSeconds)
    {
        var result = _validator.Validate(name: null, new MaintenanceModeOptions { RetryAfterSeconds = retryAfterSeconds });

        Assert.Equal("RetryAfterSeconds must be greater than zero when set.", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Retry_after_of_one_second_is_valid()
    {
        var result = _validator.Validate(name: null, new MaintenanceModeOptions { RetryAfterSeconds = 1 });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("")]
    public void Exempt_path_prefix_without_a_leading_slash_fails(string pathPrefix)
    {
        var options = new MaintenanceModeOptions();
        options.ExemptPathPrefixes.Add(pathPrefix);

        var result = _validator.Validate(name: null, options);

        Assert.Equal($"ExemptPathPrefixes entry '{pathPrefix}' must start with '/', such as '/health'.", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Invalid_options_stop_the_host_at_startup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MaintenanceMode:StatusCode"] = "200" })
            .Build();
        using var serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddPinqponqMaintenanceMode(configuration)
            .BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => serviceProvider.GetRequiredService<IOptions<MaintenanceModeOptions>>().Value);

        Assert.Contains("StatusCode must be from 400 to 599.", exception.Failures);
    }
}
