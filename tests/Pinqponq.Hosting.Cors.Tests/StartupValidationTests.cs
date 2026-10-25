using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.Hosting.Cors.Tests;

public sealed class StartupValidationTests
{
    [Fact]
    public async Task Empty_list_stops_the_host_and_names_the_setting()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => CorsHost.Start(_ => { }));

        Assert.Contains("AllowedOrigins must contain at least one origin.", exception.Message);
    }

    [Fact]
    public async Task Empty_list_stops_the_host_in_production_even_when_the_development_switch_is_on()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(
            () => CorsHost.Start(options => options.AllowAnyOriginInDevelopmentWhenEmpty = true));
    }

    [Fact]
    public async Task Empty_list_stops_the_host_in_development_when_the_switch_is_off()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(
            () => CorsHost.Start(_ => { }, environment: "Development"));
    }

    [Fact]
    public async Task Wildcard_with_credentials_stops_the_host()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => CorsHost.Start(options =>
            {
                options.AllowedOrigins = ["*"];
                options.AllowCredentials = true;
            }));

        Assert.Contains("AllowCredentials cannot be combined", exception.Message);
    }

    [Fact]
    public async Task Development_fallback_with_credentials_stops_the_host()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(
            () => CorsHost.Start(
                options =>
                {
                    options.AllowAnyOriginInDevelopmentWhenEmpty = true;
                    options.AllowCredentials = true;
                },
                environment: "Development"));
    }

    [Fact]
    public async Task Options_bound_from_configuration_are_used()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = CorsHost.AllowedOrigin,
                ["Cors:AllowCredentials"] = "true",
            })
            .Build();

        using var host = await CorsHost.Start(options =>
            configuration.GetSection("Cors").Bind(options));
        var response = await CorsHost.Get(host, CorsHost.AllowedOrigin);

        Assert.Equal("true", CorsHost.Header(response, "Access-Control-Allow-Credentials"));
    }
}
