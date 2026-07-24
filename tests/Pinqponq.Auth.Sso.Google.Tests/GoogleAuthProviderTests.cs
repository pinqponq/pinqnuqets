using FluentAssertions;
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
        Create().ProviderName.Should().Be("Google");
        GoogleAuthProvider.Name.Should().Be("Google");
    }

    [Fact]
    public async Task Empty_id_token_fails()
    {
        var result = await Create("client.apps.googleusercontent.com")
            .AuthenticateAsync(ExternalAuthRequest.FromIdToken("  "));

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("id_token");
    }

    [Fact]
    public async Task Null_request_throws()
    {
        var act = () => Create().AuthenticateAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Malformed_token_fails()
    {
        var result = await Create("client.apps.googleusercontent.com")
            .AuthenticateAsync(ExternalAuthRequest.FromIdToken("not.a.valid.jwt"));

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Invalid Google id_token");
    }

    [Fact]
    public void AddPinqponqGoogleSso_registers_provider()
    {
        var services = new ServiceCollection();
        services.AddPinqponqGoogleSso(o => o.ClientIds.Add("client.apps.googleusercontent.com"));

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IExternalAuthProvider>();
        provider.Should().BeOfType<GoogleAuthProvider>();
        provider.ProviderName.Should().Be("Google");
    }
}
