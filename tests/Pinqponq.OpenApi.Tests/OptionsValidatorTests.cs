using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.OpenApi.Tests;

public sealed class OptionsValidatorTests
{
    private static ValidateOptionsResult Validate(Action<PinqponqOpenApiOptions>? configure = null)
    {
        var options = new PinqponqOpenApiOptions();
        configure?.Invoke(options);
        return new PinqponqOpenApiOptionsValidator().Validate(null, options);
    }

    private static string[] Failures(ValidateOptionsResult result) => result.Failures?.ToArray() ?? [];

    [Fact]
    public void Defaults_are_valid()
    {
        Assert.True(Validate().Succeeded);
    }

    [Fact]
    public void Bearer_scheme_name_is_required_when_bearer_is_enabled()
    {
        var failure = Assert.Single(Failures(Validate(options => options.BearerAuthentication.SchemeName = " ")));

        Assert.Contains("BearerAuthentication.SchemeName", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Bearer_scheme_name_is_not_checked_when_bearer_is_disabled()
    {
        var result = Validate(options =>
        {
            options.BearerAuthentication.Enabled = false;
            options.BearerAuthentication.SchemeName = string.Empty;
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Api_key_header_name_is_required_when_the_scheme_is_enabled()
    {
        var failure = Assert.Single(Failures(Validate(options => options.ApiKeyAuthentication.Enabled = true)));

        Assert.Contains("ApiKeyAuthentication.HeaderName", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_key_scheme_name_is_required_when_the_scheme_is_enabled()
    {
        var failure = Assert.Single(Failures(Validate(options =>
        {
            options.ApiKeyAuthentication.Enabled = true;
            options.ApiKeyAuthentication.HeaderName = "X-Api-Key";
            options.ApiKeyAuthentication.SchemeName = string.Empty;
        })));

        Assert.Contains("ApiKeyAuthentication.SchemeName", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_key_header_name_is_not_checked_when_the_scheme_is_disabled()
    {
        Assert.True(Validate(options => options.ApiKeyAuthentication.HeaderName = null).Succeeded);
    }

    [Fact]
    public void Both_schemes_must_have_different_names()
    {
        var failure = Assert.Single(Failures(Validate(options =>
        {
            options.ApiKeyAuthentication.Enabled = true;
            options.ApiKeyAuthentication.HeaderName = "X-Api-Key";
            options.ApiKeyAuthentication.SchemeName = options.BearerAuthentication.SchemeName;
        })));

        Assert.Contains("different SchemeName", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Equal_scheme_names_are_valid_when_only_one_scheme_is_enabled()
    {
        var bearerOnly = Validate(options => options.ApiKeyAuthentication.SchemeName = options.BearerAuthentication.SchemeName);
        var apiKeyOnly = Validate(options =>
        {
            options.BearerAuthentication.Enabled = false;
            options.ApiKeyAuthentication.Enabled = true;
            options.ApiKeyAuthentication.HeaderName = "X-Api-Key";
            options.ApiKeyAuthentication.SchemeName = options.BearerAuthentication.SchemeName;
        });

        Assert.True(bearerOnly.Succeeded);
        Assert.True(apiKeyOnly.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("openapi/{documentName}.json")]
    [InlineData("/openapi/v1.json")]
    public void Document_route_must_start_with_a_slash_and_contain_the_document_name(string routePattern)
    {
        var failure = Assert.Single(Failures(Validate(options => options.OpenApiRoutePattern = routePattern)));

        Assert.Contains("OpenApiRoutePattern", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("scalar")]
    public void Scalar_route_prefix_must_start_with_a_slash(string routePrefix)
    {
        var failure = Assert.Single(Failures(Validate(options => options.ScalarRoutePrefix = routePrefix)));

        Assert.Contains("ScalarRoutePrefix", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_failure_is_reported_together()
    {
        var failures = Failures(Validate(options =>
        {
            options.ApiKeyAuthentication.Enabled = true;
            options.OpenApiRoutePattern = "/none";
            options.ScalarRoutePrefix = "none";
        }));

        Assert.Equal(3, failures.Length);
    }
}
