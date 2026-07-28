using FluentAssertions;
using Pinqponq.Identity.Otp;
using Xunit;

namespace Pinqponq.Identity.Otp.Tests;

public sealed class OtpOptionsValidatorTests
{
    private readonly OtpOptionsValidator _validator = new();

    [Fact]
    public void Short_or_empty_pepper_fails()
    {
        _validator.Validate(null, new OtpOptions { HashPepper = "" }).Succeeded.Should().BeFalse();
        _validator.Validate(null, new OtpOptions { HashPepper = "short" }).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Valid_pepper_succeeds()
    {
        _validator.Validate(null, new OtpOptions
        {
            HashPepper = "0123456789abcdef0123456789abcdef",
        }).Succeeded.Should().BeTrue();
    }
}
