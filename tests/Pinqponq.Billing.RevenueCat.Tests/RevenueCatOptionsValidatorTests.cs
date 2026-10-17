using Xunit;

namespace Pinqponq.Billing.RevenueCat.Tests;

public sealed class RevenueCatOptionsValidatorTests
{
    private readonly RevenueCatOptionsValidator _sut = new();

    [Fact]
    public void Complete_options_pass()
    {
        var result = _sut.Validate(null, new RevenueCatOptions { WebhookAuthHeader = "Bearer x", PremiumEntitlementId = "premium" });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("", "premium", nameof(RevenueCatOptions.WebhookAuthHeader))]
    [InlineData("  ", "premium", nameof(RevenueCatOptions.WebhookAuthHeader))]
    [InlineData("Bearer x", "", nameof(RevenueCatOptions.PremiumEntitlementId))]
    [InlineData("Bearer x", " ", nameof(RevenueCatOptions.PremiumEntitlementId))]
    public void Missing_required_setting_fails_and_names_it(string header, string entitlement, string expectedName)
    {
        var result = _sut.Validate(null, new RevenueCatOptions { WebhookAuthHeader = header, PremiumEntitlementId = entitlement });

        Assert.True(result.Failed);
        Assert.Contains(expectedName, result.FailureMessage);
    }

    [Fact]
    public void Defaults_are_empty_so_a_forgotten_setting_cannot_pass()
    {
        var result = _sut.Validate(null, new RevenueCatOptions());

        Assert.True(result.Failed);
    }
}
