using Xunit;

namespace Pinqponq.Auth.Sso.Firebase.Tests;

public sealed class FirebaseAuthOptionsValidatorTests
{
    private readonly FirebaseAuthOptionsValidator _validator = new();

    [Fact]
    public void Valid_options_pass()
    {
        var options = CreateValidOptions();
        options.AllowedSignInProviders.Add("apple.com");
        options.ClockSkew = TimeSpan.Zero;
        options.MinimumUnknownKeyRefreshInterval = TimeSpan.Zero;

        Assert.True(_validator.Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Missing_ProjectId_fails(string? projectId)
    {
        var options = CreateValidOptions();
        options.ProjectId = projectId;

        AssertFails(options, "ProjectId is required.");
    }

    [Fact]
    public void Empty_allowed_sign_in_provider_fails()
    {
        var options = CreateValidOptions();
        options.AllowedSignInProviders.Add("apple.com");
        options.AllowedSignInProviders.Add(" ");

        AssertFails(options, "AllowedSignInProviders must not contain empty entries.");
    }

    [Fact]
    public void Negative_clock_skew_fails()
    {
        var options = CreateValidOptions();
        options.ClockSkew = TimeSpan.FromSeconds(-1);

        AssertFails(options, "ClockSkew must not be negative.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_keys_cache_duration_fails(int seconds)
    {
        var options = CreateValidOptions();
        options.KeysCacheDurationWithoutMaxAge = TimeSpan.FromSeconds(seconds);

        AssertFails(options, "KeysCacheDurationWithoutMaxAge must be positive.");
    }

    [Fact]
    public void Negative_unknown_key_refresh_interval_fails()
    {
        var options = CreateValidOptions();
        options.MinimumUnknownKeyRefreshInterval = TimeSpan.FromSeconds(-1);

        AssertFails(options, "MinimumUnknownKeyRefreshInterval must not be negative.");
    }

    [Theory]
    [InlineData("http://keys.example.com/jwks")]
    [InlineData("/jwks")]
    public void Public_keys_uri_that_is_not_absolute_https_fails(string publicKeysUri)
    {
        var options = CreateValidOptions();
        options.PublicKeysUri = new Uri(publicKeysUri, UriKind.RelativeOrAbsolute);

        AssertFails(options, "PublicKeysUri must be an absolute HTTPS address.");
    }

    [Fact]
    public void Null_public_keys_uri_fails()
    {
        var options = CreateValidOptions();
        options.PublicKeysUri = null!;

        AssertFails(options, "PublicKeysUri must be an absolute HTTPS address.");
    }

    private static FirebaseAuthOptions CreateValidOptions() => new() { ProjectId = FirebaseTestServer.ProjectId };

    private void AssertFails(FirebaseAuthOptions options, string expectedFailure)
    {
        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(expectedFailure, result.FailureMessage);
    }
}
