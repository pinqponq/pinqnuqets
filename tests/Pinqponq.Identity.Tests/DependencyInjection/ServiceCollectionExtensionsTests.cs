using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Identity.DependencyInjection;
using Pinqponq.Identity.Jwt;
using Pinqponq.Identity.Passwords;
using Pinqponq.Identity.RefreshTokens;
using Pinqponq.Identity.Tests.RefreshTokens;
using Xunit;

namespace Pinqponq.Identity.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPinqponqIdentity_resolves_all_primitives()
    {
        var services = new ServiceCollection();
        services.AddPinqponqIdentity(jwt =>
        {
            jwt.Issuer = "pinqponq";
            jwt.Audience = "clients";
            jwt.SymmetricKey = "0123456789abcdef0123456789abcdef";
        });

        // Store is the consumer's responsibility.
        services.AddScoped<IRefreshTokenStore, InMemoryRefreshTokenStore>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetService<IJwtTokenGenerator>().Should().NotBeNull();
        sp.GetService<IJwtTokenValidator>().Should().NotBeNull();
        sp.GetService<IPasswordHasher>().Should().NotBeNull();
        sp.GetService<IRefreshTokenService>().Should().NotBeNull();
    }

    [Fact]
    public void AddPinqponqIdentity_does_not_register_a_refresh_token_store()
    {
        var services = new ServiceCollection();
        services.AddPinqponqIdentity(jwt =>
        {
            jwt.Issuer = "pinqponq";
            jwt.Audience = "clients";
            jwt.SymmetricKey = "0123456789abcdef0123456789abcdef";
        });

        services.Should().NotContain(d => d.ServiceType == typeof(IRefreshTokenStore));
    }
}
