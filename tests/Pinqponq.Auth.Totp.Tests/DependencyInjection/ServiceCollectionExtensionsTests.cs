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
        services.AddPinqponqTotp(o => o.Issuer = "Pinqponq");

        using var sp = services.BuildServiceProvider();
        var totp = sp.GetRequiredService<ITotpService>();
        totp.Should().NotBeNull();
        totp.GenerateSecret().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The configure action is optional, so the extension has to register the options
    /// services itself — nothing else in a bare collection does.
    /// </summary>
    [Fact]
    public void AddPinqponqTotp_resolves_with_defaults_when_left_unconfigured()
    {
        var services = new ServiceCollection();
        services.AddPinqponqTotp();

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ITotpService>().GenerateSecret().Should().NotBeNullOrWhiteSpace();
    }
}
