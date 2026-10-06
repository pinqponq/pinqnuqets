using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqponq.Auth.Sso.Abstractions;
using Pinqponq.Auth.Sso.Google.DependencyInjection;
using Xunit;

namespace Pinqponq.Auth.Sso.Google.Tests;

public sealed class GoogleAuthProviderTests
{
    private static GoogleAuthProvider Create(params string[] clientIds)
    {
        var options = new GoogleAuthOptions();
        foreach (var id in clientIds)
        {
            options.ClientIds.Add(id);
        }

        return new GoogleAuthProvider(Options.Create(options));
    }

    [Fact]
    public void ProviderName_is_Google()
    {
        Assert.Equal("Google", Create().ProviderName);
        Assert.Equal("Google", GoogleAuthProvider.Name);
    }

    [Fact]
    public void RequireEmailVerified_defaults_to_true()
    {
        Assert.True(new GoogleAuthOptions().RequireEmailVerified);
    }

    [Fact]
    public void RequireNonce_defaults_to_false()
    {
        Assert.False(new GoogleAuthOptions().RequireNonce);
    }

    [Fact]
    public async Task RequireNonce_true_with_empty_nonce_fails()
    {
        var options = new GoogleAuthOptions { RequireNonce = true };
        options.ClientIds.Add("client.apps.googleusercontent.com");
        var provider = new GoogleAuthProvider(Options.Create(options));

        var result = await provider.AuthenticateAsync(ExternalAuthRequest.FromIdToken("x.y.z"));

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid Google id_token.", result.Error);
    }

    [Fact]
    public async Task Empty_id_token_fails()
    {
        var result = await Create("client.apps.googleusercontent.com")
            .AuthenticateAsync(ExternalAuthRequest.FromIdToken("  "));

        Assert.False(result.Succeeded);
        Assert.Contains("id_token", result.Error);
    }

    [Fact]
    public async Task Null_request_throws()
    {
        var act = () => Create().AuthenticateAsync(null!);
        await Assert.ThrowsAnyAsync<ArgumentNullException>(act);
    }

    [Fact]
    public async Task Cancelled_token_throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Create("client.apps.googleusercontent.com")
            .AuthenticateAsync(ExternalAuthRequest.FromIdToken("x.y.z"), cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task Malformed_token_fails()
    {
        var result = await Create("client.apps.googleusercontent.com")
            .AuthenticateAsync(ExternalAuthRequest.FromIdToken("not.a.valid.jwt"));

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid Google id_token.", result.Error);
    }

    [Fact]
    public async Task Authorization_code_without_id_token_fails_explicitly()
    {
        var result = await Create("client.apps.googleusercontent.com")
            .AuthenticateAsync(ExternalAuthRequest.FromAuthorizationCode("code", "https://app/cb"));

        Assert.False(result.Succeeded);
        Assert.Contains("not supported", result.Error);
    }

    [Fact]
    public void AddPinqponqGoogleSso_registers_provider()
    {
        var services = new ServiceCollection();
        services.AddPinqponqGoogleSso(o => o.ClientIds.Add("client.apps.googleusercontent.com"));

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IExternalAuthProvider>();
        Assert.IsType<GoogleAuthProvider>(provider);
        Assert.Equal("Google", provider.ProviderName);
    }
}
