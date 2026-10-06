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

        Assert.True(result.Succeeded);
        Assert.Same(user, result.User);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Success_null_user_throws()
    {
        var act = () => ExternalAuthResult.Success(null!);
        Assert.ThrowsAny<ArgumentNullException>(act);
    }

    [Fact]
    public void Failure_sets_error()
    {
        var result = ExternalAuthResult.Failure("nope");
        Assert.False(result.Succeeded);
        Assert.Null(result.User);
        Assert.Equal("nope", result.Error);
    }

    [Fact]
    public void FromIdToken_sets_fields()
    {
        var request = ExternalAuthRequest.FromIdToken("token", "nonce-1");
        Assert.Equal("token", request.IdToken);
        Assert.Equal("nonce-1", request.Nonce);
    }

    [Fact]
    public void FromAuthorizationCode_sets_fields()
    {
        var request = ExternalAuthRequest.FromAuthorizationCode("code", "https://app/cb");
        Assert.Equal("code", request.AuthorizationCode);
        Assert.Equal("https://app/cb", request.RedirectUri);
    }
}
