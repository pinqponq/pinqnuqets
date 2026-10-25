using Xunit;

namespace Pinqponq.Hosting.Cors.Tests;

public sealed class CorsBehaviorTests
{
    private static void AllowOneOrigin(PinqponqCorsOptions options) => options.AllowedOrigins = [CorsHost.AllowedOrigin];

    [Fact]
    public async Task Allowed_origin_gets_the_cors_header()
    {
        using var host = await CorsHost.Start(AllowOneOrigin);

        var response = await CorsHost.Get(host, CorsHost.AllowedOrigin);

        Assert.Equal(CorsHost.AllowedOrigin, CorsHost.Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Other_origin_gets_no_cors_header()
    {
        using var host = await CorsHost.Start(AllowOneOrigin);

        var response = await CorsHost.Get(host, CorsHost.OtherOrigin);

        Assert.Null(CorsHost.Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Origin_match_ignores_letter_case_of_the_configured_origin()
    {
        using var host = await CorsHost.Start(options => options.AllowedOrigins = ["https://APP.example.com"]);

        var response = await CorsHost.Get(host, CorsHost.AllowedOrigin);

        Assert.Equal(CorsHost.AllowedOrigin, CorsHost.Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_without_a_max_age_carries_no_max_age_header()
    {
        using var host = await CorsHost.Start(AllowOneOrigin);

        var response = await CorsHost.Preflight(host, CorsHost.AllowedOrigin);

        Assert.Null(CorsHost.Header(response, "Access-Control-Max-Age"));
    }

    [Fact]
    public async Task Preflight_honours_the_max_age()
    {
        using var host = await CorsHost.Start(options =>
        {
            AllowOneOrigin(options);
            options.PreflightMaxAge = TimeSpan.FromMinutes(10);
        });

        var response = await CorsHost.Preflight(host, CorsHost.AllowedOrigin);

        Assert.Equal("600", CorsHost.Header(response, "Access-Control-Max-Age"));
    }

    [Fact]
    public async Task Credentials_flag_emits_allow_credentials()
    {
        using var host = await CorsHost.Start(options =>
        {
            AllowOneOrigin(options);
            options.AllowCredentials = true;
        });

        var response = await CorsHost.Get(host, CorsHost.AllowedOrigin);

        Assert.Equal("true", CorsHost.Header(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Credentials_are_off_by_default()
    {
        using var host = await CorsHost.Start(AllowOneOrigin);

        var response = await CorsHost.Get(host, CorsHost.AllowedOrigin);

        Assert.Null(CorsHost.Header(response, "Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Any_header_and_method_are_allowed_by_default()
    {
        using var host = await CorsHost.Start(AllowOneOrigin);

        var response = await CorsHost.Preflight(host, CorsHost.AllowedOrigin, method: "DELETE", headers: "x-custom");

        Assert.Equal("x-custom", CorsHost.Header(response, "Access-Control-Allow-Headers"));
        Assert.Equal("DELETE", CorsHost.Header(response, "Access-Control-Allow-Methods"));
    }

    [Fact]
    public async Task Listed_methods_and_headers_limit_the_preflight_answer()
    {
        using var host = await CorsHost.Start(options =>
        {
            AllowOneOrigin(options);
            options.AllowedMethods = ["GET"];
            options.AllowedHeaders = ["x-allowed"];
        });

        var allowed = await CorsHost.Preflight(host, CorsHost.AllowedOrigin, method: "GET", headers: "x-allowed");
        var wrongMethod = await CorsHost.Preflight(host, CorsHost.AllowedOrigin, method: "DELETE");
        var wrongHeader = await CorsHost.Preflight(host, CorsHost.AllowedOrigin, headers: "x-other");

        Assert.Equal("GET", CorsHost.Header(allowed, "Access-Control-Allow-Methods"));
        Assert.Equal("x-allowed", CorsHost.Header(allowed, "Access-Control-Allow-Headers"));
        Assert.Equal("GET", CorsHost.Header(wrongMethod, "Access-Control-Allow-Methods"));
        Assert.NotEqual("x-other", CorsHost.Header(wrongHeader, "Access-Control-Allow-Headers"));
    }

    [Fact]
    public async Task Exposed_headers_are_sent()
    {
        using var host = await CorsHost.Start(options =>
        {
            AllowOneOrigin(options);
            options.ExposedHeaders = ["x-total"];
        });

        var response = await CorsHost.Get(host, CorsHost.AllowedOrigin);

        Assert.Equal("x-total", CorsHost.Header(response, "Access-Control-Expose-Headers"));
    }

    [Fact]
    public async Task Wildcard_allows_every_origin()
    {
        using var host = await CorsHost.Start(options => options.AllowedOrigins = ["*"]);

        var response = await CorsHost.Get(host, CorsHost.OtherOrigin);

        Assert.Equal("*", CorsHost.Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Named_policy_applies_only_through_use_cors_with_its_name()
    {
        using var host = await CorsHost.Start(
            options =>
            {
                AllowOneOrigin(options);
                options.PolicyName = "browser";
            },
            policyName: "browser");

        var response = await CorsHost.Get(host, CorsHost.AllowedOrigin);

        Assert.Equal(CorsHost.AllowedOrigin, CorsHost.Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Empty_list_allows_any_origin_in_development_when_the_switch_is_on()
    {
        using var host = await CorsHost.Start(
            options => options.AllowAnyOriginInDevelopmentWhenEmpty = true,
            environment: "Development");

        var response = await CorsHost.Get(host, CorsHost.OtherOrigin);

        Assert.Equal("*", CorsHost.Header(response, "Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Listed_origins_still_apply_in_development_when_the_switch_is_on()
    {
        using var host = await CorsHost.Start(
            options =>
            {
                AllowOneOrigin(options);
                options.AllowAnyOriginInDevelopmentWhenEmpty = true;
            },
            environment: "Development");

        var response = await CorsHost.Get(host, CorsHost.OtherOrigin);

        Assert.Null(CorsHost.Header(response, "Access-Control-Allow-Origin"));
    }
}
