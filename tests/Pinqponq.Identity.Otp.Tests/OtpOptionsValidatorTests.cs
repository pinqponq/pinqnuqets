using Pinqponq.Identity.Otp;
using Xunit;

namespace Pinqponq.Identity.Otp.Tests;

public sealed class OtpOptionsValidatorTests
{
    private readonly OtpOptionsValidator _validator = new();

    [Fact]
    public void Short_or_empty_pepper_fails()
    {
        Assert.False(_validator.Validate(null, new OtpOptions { HashPepper = "" }).Succeeded);
        Assert.False(_validator.Validate(null, new OtpOptions { HashPepper = "short" }).Succeeded);
    }

    [Fact]
    public void Valid_pepper_succeeds()
    {
        Assert.True(_validator.Validate(null, new OtpOptions
        {
            HashPepper = "0123456789abcdef0123456789abcdef",
        }).Succeeded);
    }
}
