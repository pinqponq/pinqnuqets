using Xunit;

namespace Pinqponq.Auth.ApiKey.Tests;

public sealed class ApiKeyOptionsValidatorTests
{
    [Fact]
    public void Header_name_set_succeeds()
    {
        var result = new ApiKeyOptionsValidator("internal").Validate("internal", new ApiKeyOptions { HeaderName = "X-Internal-Key" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Other_names_are_skipped()
    {
        var result = new ApiKeyOptionsValidator("internal").Validate("partner", new ApiKeyOptions());

        Assert.True(result.Skipped);
    }

    [Fact]
    public void Empty_header_name_fails()
    {
        var result = new ApiKeyOptionsValidator("internal").Validate("internal", new ApiKeyOptions());

        Assert.True(result.Failed);
        Assert.Equal("HeaderName is required for API key 'internal'.", result.FailureMessage);
    }
}
