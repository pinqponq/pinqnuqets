using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqponq.Auth.ApiKey.DependencyInjection;
using Xunit;

namespace Pinqponq.Auth.ApiKey.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void Registers_named_options()
    {
        var services = new ServiceCollection();
        services.AddPinqponqApiKey("internal", options =>
        {
            options.HeaderName = "X-Internal-Key";
            options.ApiKey = "s3cret";
        });
        using var provider = services.BuildServiceProvider();

        var apiKeyOptions = provider.GetRequiredService<IOptionsMonitor<ApiKeyOptions>>().Get("internal");

        Assert.Equal("X-Internal-Key", apiKeyOptions.HeaderName);
        Assert.Equal("s3cret", apiKeyOptions.ApiKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_header_name_fails_validation(string headerName)
    {
        var services = new ServiceCollection();
        services.AddPinqponqApiKey("internal", options =>
        {
            options.HeaderName = headerName;
            options.ApiKey = "s3cret";
        });
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<ApiKeyOptions>>().Get("internal"));

        Assert.Contains("HeaderName is required for API key 'internal'.", exception.Message);
    }

    [Fact]
    public void Empty_header_name_stops_startup()
    {
        var services = new ServiceCollection();
        services.AddPinqponqApiKey("internal", options => options.ApiKey = "s3cret");
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void Missing_key_passes_validation()
    {
        var services = new ServiceCollection();
        services.AddPinqponqApiKey("internal", options => options.HeaderName = "X-Internal-Key");
        using var provider = services.BuildServiceProvider();

        var apiKeyOptions = provider.GetRequiredService<IOptionsMonitor<ApiKeyOptions>>().Get("internal");

        Assert.Null(apiKeyOptions.ApiKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Blank_name_is_rejected(string name)
    {
        var services = new ServiceCollection();

        Assert.ThrowsAny<ArgumentException>(() => services.AddPinqponqApiKey(name, options => { }));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddPinqponqApiKey(null!, "internal", options => { }));
        Assert.Throws<ArgumentNullException>(() => services.AddPinqponqApiKey(null!, options => { }));
        Assert.Throws<ArgumentNullException>(() => services.AddPinqponqApiKey("internal", null!));
    }
}
