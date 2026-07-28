using System.Security.Claims;
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

    [Fact]
    public async Task Scoped_revocation_store_resolves_with_ValidateScopes()
    {
        var services = new ServiceCollection();
        services.AddPinqponqIdentity(jwt =>
        {
            jwt.Issuer = "pinqponq";
            jwt.Audience = "clients";
            jwt.SymmetricKey = "0123456789abcdef0123456789abcdef";
        });
        services.AddScoped<IAccessTokenRevocationStore, InMemoryRevocationStore>();
        services.AddScoped<IRefreshTokenStore, InMemoryRefreshTokenStore>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        using var scope = provider.CreateScope();
        var gen = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();
        var val = scope.ServiceProvider.GetRequiredService<IJwtTokenValidator>();

        var token = gen.GenerateToken([new Claim(ClaimTypes.NameIdentifier, "u1")]);
        (await val.ValidateAsync(token)).Should().NotBeNull();
    }

    private sealed class InMemoryRevocationStore : IAccessTokenRevocationStore
    {
        public Task RevokeAsync(
            string jti,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
