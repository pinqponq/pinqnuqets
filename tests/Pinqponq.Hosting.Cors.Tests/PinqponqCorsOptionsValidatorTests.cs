using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Pinqponq.Hosting.Cors.Tests;

public sealed class PinqponqCorsOptionsValidatorTests
{
    private static PinqponqCorsOptionsValidator ValidatorFor(string environment) => new(new FakeHostEnvironment(environment));

    private static PinqponqCorsOptions Options(params string[] origins) => new() { AllowedOrigins = origins };

    [Theory]
    [InlineData("https://app.example.com")]
    [InlineData("http://localhost:3000")]
    [InlineData("https://app.example.com:8443")]
    [InlineData("HTTPS://APP.EXAMPLE.COM")]
    public void Valid_origin_succeeds(string origin)
    {
        var result = ValidatorFor("Production").Validate(name: null, Options(origin));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("https://app.example.com/")]
    [InlineData("https://app.example.com/path")]
    [InlineData("https://app.example.com?x=1")]
    [InlineData("https://user@app.example.com")]
    [InlineData("ftp://app.example.com")]
    [InlineData("app.example.com")]
    [InlineData("")]
    [InlineData("  ")]
    public void Invalid_origin_fails_and_is_named(string origin)
    {
        var result = ValidatorFor("Production").Validate(name: null, Options(origin));

        Assert.True(result.Failed);
        Assert.Equal(
            [$"AllowedOrigins entry '{origin}' must be an http or https origin without a path, such as 'https://app.example.com'."],
            result.Failures);
    }

    [Fact]
    public void Every_invalid_origin_is_reported()
    {
        var result = ValidatorFor("Production").Validate(name: null, Options("https://a.example.com/", "nonsense"));

        Assert.Equal(2, result.Failures!.Count());
    }

    [Fact]
    public void Wildcard_alone_succeeds()
    {
        var result = ValidatorFor("Production").Validate(name: null, Options("*"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Wildcard_next_to_another_origin_fails()
    {
        var result = ValidatorFor("Production").Validate(name: null, Options("*", "https://app.example.com"));

        Assert.Equal(["AllowedOrigins must contain only '*' when it allows every origin."], result.Failures);
    }

    [Fact]
    public void Empty_list_fails()
    {
        var result = ValidatorFor("Production").Validate(name: null, Options());

        Assert.Equal(["AllowedOrigins must contain at least one origin."], result.Failures);
    }

    [Fact]
    public void Empty_list_succeeds_in_development_with_the_switch()
    {
        var options = Options();
        options.AllowAnyOriginInDevelopmentWhenEmpty = true;

        var result = ValidatorFor("Development").Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Wildcard_with_credentials_fails()
    {
        var options = Options("*");
        options.AllowCredentials = true;

        var result = ValidatorFor("Production").Validate(name: null, options);

        Assert.Equal(["AllowCredentials cannot be combined with an origin list that allows every origin."], result.Failures);
    }

    [Fact]
    public void Listed_origin_with_credentials_succeeds()
    {
        var options = Options("https://app.example.com");
        options.AllowCredentials = true;

        var result = ValidatorFor("Production").Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Preflight_max_age_must_be_greater_than_zero(int seconds)
    {
        var options = Options("https://app.example.com");
        options.PreflightMaxAge = TimeSpan.FromSeconds(seconds);

        var result = ValidatorFor("Production").Validate(name: null, options);

        Assert.Equal(["PreflightMaxAge must be greater than zero."], result.Failures);
    }

    [Fact]
    public void Blank_policy_name_fails()
    {
        var options = Options("https://app.example.com");
        options.PolicyName = " ";

        var result = ValidatorFor("Production").Validate(name: null, options);

        Assert.Equal(["PolicyName must not be blank."], result.Failures);
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
