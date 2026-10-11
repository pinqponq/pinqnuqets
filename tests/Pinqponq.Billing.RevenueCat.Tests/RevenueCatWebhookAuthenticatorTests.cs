using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.Billing.RevenueCat.Tests;

public sealed class RevenueCatWebhookAuthenticatorTests
{
    private static RevenueCatWebhookAuthenticator Create(string secret) =>
        new(Options.Create(new RevenueCatOptions { WebhookAuthHeader = secret, PremiumEntitlementId = "premium" }));

    [Fact]
    public void Matching_header_is_authorized() => Assert.True(Create("Bearer s3cret").IsAuthorized("Bearer s3cret"));

    [Theory]
    [InlineData("Bearer s3creT")]
    [InlineData("Bearer s3cre")]
    [InlineData("Bearer s3cret ")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_headers_are_refused(string? header) => Assert.False(Create("Bearer s3cret").IsAuthorized(header));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Unconfigured_secret_refuses_everything(string secret)
    {
        var sut = Create(secret);

        Assert.False(sut.IsAuthorized(secret));
        Assert.False(sut.IsAuthorized("anything"));
    }

    [Fact]
    public void Header_comparison_is_case_sensitive() => Assert.False(Create("Bearer abc").IsAuthorized("bearer abc"));

    [Fact]
    public void Multibyte_headers_compare_by_value() => Assert.True(Create("Bearer şifre").IsAuthorized("Bearer şifre"));
}
