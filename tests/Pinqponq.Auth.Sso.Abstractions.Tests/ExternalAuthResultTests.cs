using FluentAssertions;
using Xunit;

namespace Pinqponq.Auth.Sso.Abstractions.Tests;

public sealed class ExternalAuthResultTests
{
    [Fact]
    public void Success_sets_user_and_succeeded()
    {
        var user = new ExternalUserInfo
        {
            Subject = "sub-1",
            Provider = "Google",
            Email = "a@b.com",
        };

        var result = ExternalAuthResult.Success(user);

        result.Succeeded.Should().BeTrue();
        result.User.Should().BeSameAs(user);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Success_null_user_throws()
    {
        var act = () => ExternalAuthResult.Success(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Failure_sets_error()
    {
        var result = ExternalAuthResult.Failure("nope");
        result.Succeeded.Should().BeFalse();
        result.User.Should().BeNull();
        result.Error.Should().Be("nope");
    }

    [Fact]
    public void FromIdToken_sets_fields()
    {
        var request = ExternalAuthRequest.FromIdToken("token", "nonce-1");
        request.IdToken.Should().Be("token");
        request.Nonce.Should().Be("nonce-1");
    }

    [Fact]
    public void FromAuthorizationCode_sets_fields()
    {
        var request = ExternalAuthRequest.FromAuthorizationCode("code", "https://app/cb");
        request.AuthorizationCode.Should().Be("code");
        request.RedirectUri.Should().Be("https://app/cb");
    }
}
