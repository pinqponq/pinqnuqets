using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Auth.Totp.DependencyInjection;
using Xunit;

namespace Pinqponq.Auth.Totp.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPinqponqTotp_registers_service()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITotpReplayStore, AllowAllReplayStore>();
        services.AddPinqponqTotp(o => o.Issuer = "Pinqponq");

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        totp.Should().NotBeNull();
        totp.GenerateSecret().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Scoped_replay_store_resolves_with_ValidateScopes()
    {
        var services = new ServiceCollection();
        services.AddScoped<ITotpReplayStore, AllowAllReplayStore>();
        services.AddPinqponqTotp(o => o.Issuer = "Pinqponq");

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        using var scope = provider.CreateScope();
        var totp = scope.ServiceProvider.GetRequiredService<ITotpService>();
        var secret = totp.GenerateSecret();
        var code = totp.ComputeCode(secret);

        (await totp.ValidateAsync(secret, code, "user-1")).Should().BeTrue();
    }

    private sealed class AllowAllReplayStore : ITotpReplayStore
    {
        public Task<bool> TryAcceptAsync(
            string subjectKey,
            long counter,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
